using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Api.Extensions;
using Khylo.Common.Configuration;
using Khylo.Controller.Dvd;
using Khylo.Controller.Entities;
using Khylo.Controller.Library;
using Khylo.Controller.MediaEncoding;
using Khylo.Model.Dto;
using Khylo.Model.Entities;
using Khylo.Model.MediaInfo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>A title on a disc.</summary>
public class DiscTitle
{
    /// <summary>Gets or sets the number.</summary>
    public int Number { get; set; }

    /// <summary>Gets or sets a value indicating whether this is the main feature.</summary>
    public bool IsMain { get; set; }

    /// <summary>Gets or sets the media source id to play this title.</summary>
    public string MediaSourceId { get; set; } = string.Empty;

    /// <summary>Gets or sets the length in seconds.</summary>
    public double Seconds { get; set; }

    /// <summary>Gets or sets the length in ticks.</summary>
    public long RunTimeTicks { get; set; }

    /// <summary>Gets or sets the picture width.</summary>
    public int Width { get; set; }

    /// <summary>Gets or sets the picture height.</summary>
    public int Height { get; set; }

    /// <summary>Gets or sets a value indicating whether the picture is widescreen.</summary>
    public bool IsWidescreen { get; set; }

    /// <summary>Gets or sets the chapters.</summary>
    public IReadOnlyList<DiscChapter> Chapters { get; set; } = [];

    /// <summary>Gets or sets the audio tracks.</summary>
    public IReadOnlyList<DiscAudio> Audio { get; set; } = [];

    /// <summary>Gets or sets the subtitle tracks.</summary>
    public IReadOnlyList<DiscSubtitle> Subtitles { get; set; } = [];
}
