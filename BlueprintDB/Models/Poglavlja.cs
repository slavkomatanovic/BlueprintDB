using System;
using System.Collections.Generic;

namespace Blueprint.App.Models;

public partial class Poglavlja
{
    public int Idpoglavlja { get; set; }

    public int Iddokumenta { get; set; }

    public string? Nazivpoglavlja { get; set; }

    public bool Postoji { get; set; }

    public string? Verzija { get; set; }

    public string? Korisnik { get; set; }

    public DateTime? Datumupisa { get; set; }

    public bool? Skriven { get; set; }

    public decimal? Vremenskipecat { get; set; }
}
