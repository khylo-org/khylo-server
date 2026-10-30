using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Api.Models.KhyloSetupDtos;
using Khylo.Common.Api;
using Khylo.Common.Configuration;
using Khylo.Controller.Configuration;
using Khylo.Controller.MediaEncoding;
using Khylo.Model.Configuration;
using Khylo.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// Helpers for Khylo's first-run setup.
/// </summary>
[Route("Khylo/Setup")]
[Authorize(Policy = Policies.FirstTimeSetupOrElevated)]
public class KhyloSetupController : BaseKhyloApiController
{
    private static readonly SemaphoreSlim _detectLock = new(1, 1);
    private static List<HardwareOption>? _detected;

    private readonly IMediaEncoder _mediaEncoder;
    private readonly IServerConfigurationManager _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloSetupController"/> class.
    /// </summary>
    /// <param name="mediaEncoder">The media encoder.</param>
    /// <param name="config">The server configuration.</param>
    public KhyloSetupController(IMediaEncoder mediaEncoder, IServerConfigurationManager config)
    {
        _mediaEncoder = mediaEncoder;
        _config = config;
    }

    /// <summary>
    /// Finds which kinds of graphics hardware can convert video on this computer, by trying each one
    /// for a fraction of a second.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">What was found, and which one is in use.</response>
    /// <returns>The hardware options.</returns>
    [HttpGet("Hardware")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<HardwareDetection>> DetectHardware(CancellationToken cancellationToken)
    {
        await _detectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_detected is null)
            {
                var options = new List<HardwareOption>();
                foreach (var (type, name, args) in Candidates())
                {
                    options.Add(new HardwareOption { Type = type, Name = name, Works = await TryEncoderAsync(args, cancellationToken).ConfigureAwait(false) });
                }

                _detected = options;
            }

            return new HardwareDetection
            {
                Options = _detected,
                Current = _config.GetEncodingOptions().HardwareAccelerationType.ToString().ToLowerInvariant()
            };
        }
        finally
        {
            _detectLock.Release();
        }
    }

    /// <summary>
    /// Uses a kind of graphics hardware for converting video, or none ("none").
    /// </summary>
    /// <param name="request">The choice.</param>
    /// <response code="204">Saved.</response>
    /// <response code="400">Not a kind Khylo knows.</response>
    /// <returns>A <see cref="NoContentResult"/>.</returns>
    [HttpPost("Hardware")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult SetHardware([FromBody] HardwareOption request)
    {
        if (!Enum.TryParse<HardwareAccelerationType>(request.Type, true, out var type))
        {
            return BadRequest("Unknown hardware type.");
        }

        var options = _config.GetEncodingOptions();
        options.HardwareAccelerationType = type;
        options.EnableHardwareEncoding = type != HardwareAccelerationType.none;

        // Decoding the two formats nearly every library is made of, which all of these handle
        options.HardwareDecodingCodecs = type == HardwareAccelerationType.none ? [] : ["h264", "hevc"];
        _config.SaveConfiguration("encoding", options);
        return NoContent();
    }

    private static IEnumerable<(string Type, string Name, string Args)> Candidates()
    {
        const string Source = "-f lavfi -i color=c=black:s=320x240:d=0.3";
        if (OperatingSystem.IsMacOS())
        {
            yield return ("videotoolbox", "Apple VideoToolbox", $"{Source} -c:v h264_videotoolbox");
            yield break;
        }

        yield return ("nvenc", "NVIDIA graphics (NVENC)", $"{Source} -c:v h264_nvenc");
        if (OperatingSystem.IsWindows())
        {
            yield return ("qsv", "Intel Quick Sync", $"{Source} -c:v h264_qsv");
            yield return ("amf", "AMD graphics (AMF)", $"{Source} -c:v h264_amf");
        }
        else if (System.IO.File.Exists("/dev/dri/renderD128"))
        {
            yield return ("qsv", "Intel Quick Sync", $"-init_hw_device qsv=hw:/dev/dri/renderD128 -filter_hw_device hw {Source} -vf format=nv12,hwupload=extra_hw_frames=16 -c:v h264_qsv");
            yield return ("vaapi", "Intel or AMD graphics (VA-API)", $"-vaapi_device /dev/dri/renderD128 {Source} -vf format=nv12,hwupload -c:v h264_vaapi");
        }
    }

    // Encodes a fraction of a second of black video; works only when the hardware is really there
    private async Task<bool> TryEncoderAsync(string args, CancellationToken cancellationToken)
    {
        var path = _mediaEncoder.EncoderPath;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(path, "-hide_banner -loglevel error " + args + " -f null -")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                }
            };
            process.Start();
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeSpan.FromSeconds(15));
            var stderr = process.StandardError.ReadToEndAsync(limit.Token);
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(true);
                return false;
            }

            await stderr.ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
