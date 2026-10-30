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

/// <summary>What a disc holds.</summary>
public class DiscInfo
{
    /// <summary>Gets or sets a value indicating whether the disc is copy-protected and can't be played.</summary>
    public bool IsScrambled { get; set; }

    /// <summary>Gets or sets a value indicating whether the disc has a menu picture.</summary>
    public bool HasMenuImage { get; set; }

    /// <summary>Gets or sets the main feature's title number.</summary>
    public int MainTitle { get; set; }

    /// <summary>Gets or sets the titles.</summary>
    public IReadOnlyList<DiscTitle> Titles { get; set; } = [];
}
