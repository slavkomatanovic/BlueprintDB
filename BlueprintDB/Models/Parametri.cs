using System;
using System.Collections.Generic;

namespace Blueprint.App.Models;

public partial class Parametri
{
    public int Idparametra { get; set; }

    public int Idpoglavlja { get; set; }

    public string? Nazivparametra { get; set; }

    public string? Podrazumijevano { get; set; }

    public string? Ocitano { get; set; }

    public string? Verzija { get; set; }
}
