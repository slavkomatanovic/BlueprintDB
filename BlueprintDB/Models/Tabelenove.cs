using System;
using System.Collections.Generic;

namespace Blueprint.App.Models;

public partial class Tabelenove
{
    public int Idtabele { get; set; }

    public int Idprograma { get; set; }

    public string? Nazivtabele { get; set; }

    public bool Cijelatabela { get; set; }

    public string? Verzija { get; set; }

    public string? Korisnik { get; set; }

    public DateTime? Datumupisa { get; set; }

    public bool? Skriven { get; set; }

    public decimal? Vremenskipecat { get; set; }
}
