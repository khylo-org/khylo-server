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

/// <summary>A subtitle track.</summary>
public class DiscSubtitle
{
    /// <summary>Gets or sets the position.</summary>
    public int Index { get; set; }

    /// <summary>Gets or sets the language code.</summary>
    public string? Language { get; set; }

    /// <summary>Gets or sets the language name.</summary>
    public string? LanguageName { get; set; }

    /// <summary>Gets or sets a note.</summary>
    public string? Note { get; set; }
}
