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

/// <summary>A chapter of a title.</summary>
public class DiscChapter
{
    /// <summary>Gets or sets the number, from 1.</summary>
    public int Number { get; set; }

    /// <summary>Gets or sets the start in seconds.</summary>
    public double StartSeconds { get; set; }

    /// <summary>Gets or sets the start in ticks.</summary>
    public long StartPositionTicks { get; set; }
}
