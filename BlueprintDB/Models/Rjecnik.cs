using System;
using System.Collections.Generic;

namespace Blueprint.App.Models;

public partial class Rjecnik
{
    public int Idrjecnik { get; set; }

    public int Idjezik { get; set; }

    public string? Original { get; set; }

    public string? Prijevod { get; set; }

    public string? Korisnik { get; set; }

    public DateTime? Datumupisa { get; set; }

    public bool? Skriven { get; set; }

    public decimal? Vremenskipecat { get; set; }
}
