using System.Windows;

namespace Blueprint.App;

public partial class SqlPreviewWindow : Window
{
    public bool Confirmed { get; private set; }

    public SqlPreviewWindow(string sql, Window? owner = null)
    {
        InitializeComponent();
        Owner    = owner;
        txtSql.Text = sql;
    }

    private void BtnIzvrsi_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void BtnOtkazi_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }

    /// <summary>
    /// Prikazuje SQL preview i vraća true ako korisnik klikne Izvrši.
    /// </summary>
    public static bool Show(string sql, Window? owner = null)
    {
        var win = new SqlPreviewWindow(sql, owner);
        win.ShowDialog();
        return win.Confirmed;
    }
}
