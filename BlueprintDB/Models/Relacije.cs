using System;
using System.Collections.Generic;

namespace Blueprint.App.Models;

public partial class Relacije
{
    public int Idrelacije { get; set; }

    public int Idprograma { get; set; }

    public string? Tabelal { get; set; }

    public string? Tabelad { get; set; }

    public string? Polje { get; set; }

    public string? Nazivrelacije { get; set; }

    public bool Updatedeletecascade { get; set; }

    public string? Verzija { get; set; }

    public string? Korisnik { get; set; }

    public DateTime? Datumupisa { get; set; }

    public bool? Skriven { get; set; }

    public decimal? Vremenskipecat { get; set; }
}
