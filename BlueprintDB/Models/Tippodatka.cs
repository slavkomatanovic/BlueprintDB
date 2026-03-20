using System;
using System.Collections.Generic;

namespace Blueprint.App.Models;

public partial class Tippodatka
{
    public string? Tippodatka1 { get; set; }

    public string? Korisnik { get; set; }

    public DateTime? Datumupisa { get; set; }

    public bool? Skriven { get; set; }

    public decimal? Vremenskipecat { get; set; }
}
