using System;
using System.Collections.Generic;

namespace Blueprint.App.Models;

public partial class Dokumenti
{
    public int Iddokumenta { get; set; }

    public int Idprograma { get; set; }

    public string? Nazivdokumenta { get; set; }

    public bool Postoji { get; set; }

    public string? Verzija { get; set; }

    public string? Korisnik { get; set; }

    public DateTime? Datumupisa { get; set; }

    public bool? Skriven { get; set; }

    public decimal? Vremenskipecat { get; set; }
}
