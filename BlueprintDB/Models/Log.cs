using System;

namespace Blueprint.App.Models;

public partial class Log
{
    public int Idlog { get; set; }
    public DateTime? Datumvrijeme { get; set; }
    public string? Nivo { get; set; }
    public string? Kategorija { get; set; }
    public string Poruka { get; set; } = "";
    public string? Detalji { get; set; }
    public string? Sqlkod { get; set; }
    public string? Backend { get; set; }
    public int? Idprogram { get; set; }
    public string? Korisnik { get; set; }
    public string? Masina { get; set; }
}
