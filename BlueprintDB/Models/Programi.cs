using System;
using System.Collections.Generic;

namespace Blueprint.App.Models;

public partial class Programi
{
    public int Idprograma { get; set; }

    public string? Nazivprograma { get; set; }

    public string? Verzija { get; set; }

    public string? Korisnik { get; set; }

    public DateTime? Datumupisa { get; set; }

    public bool? Skriven { get; set; }

    public decimal? Vremenskipecat { get; set; }
}
