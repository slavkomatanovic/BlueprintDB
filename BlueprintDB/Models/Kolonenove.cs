using System;
using System.Collections.Generic;

namespace Blueprint.App.Models;

public partial class Kolonenove
{
    public int Idkolone { get; set; }

    public int Idtabele { get; set; }

    public string? Nazivkolone { get; set; }

    public string? Tippodatka { get; set; }

    public string? Default { get; set; }

    public string? Fieldsize { get; set; }

    public string? Allownull { get; set; }

    public string? Indexed { get; set; }

    public bool Key { get; set; }

    public string? Verzija { get; set; }

    public string? Korisnik { get; set; }

    public DateTime? Datumupisa { get; set; }

    public bool? Skriven { get; set; }

    public decimal? Vremenskipecat { get; set; }
}
