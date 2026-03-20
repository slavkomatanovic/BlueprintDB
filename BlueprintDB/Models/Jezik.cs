using System;
using System.Collections.Generic;

namespace Blueprint.App.Models;

public partial class Jezik
{
    public int Idjezik { get; set; }

    public string? Nazivjezika { get; set; }

    public bool? Podrazumijevani { get; set; }

    public DateTime? Datumupisa { get; set; }

    public string? Korisnik { get; set; }

    public bool? Skriven { get; set; }

    public decimal? Vremenskipecat { get; set; }
}
