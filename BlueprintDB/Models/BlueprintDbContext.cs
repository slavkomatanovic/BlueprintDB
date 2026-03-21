using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.EntityFrameworkCore;

namespace Blueprint.App.Models;

public partial class BlueprintDbContext : DbContext
{
    public BlueprintDbContext()
    {
    }

    public BlueprintDbContext(DbContextOptions<BlueprintDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Dokumenti> Dokumentis { get; set; }

    public virtual DbSet<Jezik> Jeziks { get; set; }

    public virtual DbSet<Kolone> Kolones { get; set; }

    public virtual DbSet<Kolonenove> Kolonenoves { get; set; }

    public virtual DbSet<Log> Logs { get; set; }

    public virtual DbSet<Parametri> Parametris { get; set; }

    public virtual DbSet<Poglavlja> Poglavljas { get; set; }

    public virtual DbSet<Programi> Programis { get; set; }

    public virtual DbSet<Promjenanazivatabela> Promjenanazivatabelas { get; set; }

    public virtual DbSet<Putanje> Putanjes { get; set; }

    public virtual DbSet<Relacije> Relacijes { get; set; }

    public virtual DbSet<Rjecnik> Rjecniks { get; set; }

    public virtual DbSet<Tabele> Tabeles { get; set; }

    public virtual DbSet<Tabelenove> Tabelenoves { get; set; }

    public virtual DbSet<Tempkolonet1> Tempkolonet1s { get; set; }

    public virtual DbSet<Tempkolonet2> Tempkolonet2s { get; set; }

    public virtual DbSet<Tippodatka> Tippodatkas { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={GetDatabasePath()}");
    }

    /// <summary>
    /// Returns the full path to BlueprintMetadata.sqlite.
    /// Stored in %APPDATA%\BlueprintDB\ so the app works without admin rights
    /// when installed in Program Files.
    /// </summary>
    public static string GetDatabasePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = Path.Combine(appData, "BlueprintDB");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "BlueprintMetadata.sqlite");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Dokumenti>(entity =>
        {
            entity.HasKey(e => e.Iddokumenta);

            entity.ToTable("dokumenti");

            entity.HasIndex(e => e.Iddokumenta, "dokumentiiddokumenta172838").IsUnique();

            entity.HasIndex(e => e.Idprograma, "dokumentiidprograma172838");

            entity.Property(e => e.Iddokumenta).HasColumnName("iddokumenta");
            entity.Property(e => e.Datumupisa)
                .HasColumnType("DATE")
                .HasColumnName("datumupisa");
            entity.Property(e => e.Idprograma)
                .HasDefaultValueSql("0")
                .HasColumnName("idprograma");
            entity.Property(e => e.Korisnik)
                .HasColumnType("CHAR(50)")
                .HasColumnName("korisnik");
            entity.Property(e => e.Nazivdokumenta)
                .HasColumnType("CHAR(255)")
                .HasColumnName("nazivdokumenta");
            entity.Property(e => e.Postoji)
                .HasColumnType("BOOLEAN")
                .HasColumnName("postoji");
            entity.Property(e => e.Skriven)
                .HasDefaultValueSql("0")
                .HasColumnType("BOOLEAN")
                .HasColumnName("skriven");
            entity.Property(e => e.Verzija)
                .HasColumnType("CHAR(10)")
                .HasColumnName("verzija");
            entity.Property(e => e.Vremenskipecat)
                .HasDefaultValueSql("0")
                .HasColumnType("NUMERIC(15)")
                .HasColumnName("vremenskipecat");
        });

        modelBuilder.Entity<Jezik>(entity =>
        {
            entity.HasKey(e => e.Idjezik);

            entity.ToTable("jezik");

            entity.HasIndex(e => e.Idjezik, "jezikidjezik172840").IsUnique();

            entity.Property(e => e.Idjezik).HasColumnName("idjezik");
            entity.Property(e => e.Datumupisa)
                .HasColumnType("DATE")
                .HasColumnName("datumupisa");
            entity.Property(e => e.Korisnik)
                .HasColumnType("CHAR(50)")
                .HasColumnName("korisnik");
            entity.Property(e => e.Nazivjezika)
                .HasColumnType("CHAR(50)")
                .HasColumnName("nazivjezika");
            entity.Property(e => e.Podrazumijevani)
                .HasDefaultValueSql("0")
                .HasColumnType("BOOLEAN")
                .HasColumnName("podrazumijevani");
            entity.Property(e => e.Skriven)
                .HasDefaultValueSql("0")
                .HasColumnType("BOOLEAN")
                .HasColumnName("skriven");
            entity.Property(e => e.Vremenskipecat)
                .HasDefaultValueSql("0")
                .HasColumnType("NUMERIC(15)")
                .HasColumnName("vremenskipecat");
        });

        modelBuilder.Entity<Kolone>(entity =>
        {
            entity.HasKey(e => e.Idkolone);

            entity.ToTable("kolone");

            entity.HasIndex(e => e.Idkolone, "koloneidkolone172838").IsUnique();

            entity.HasIndex(e => e.Idtabele, "koloneidtabele172838");

            entity.Property(e => e.Idkolone).HasColumnName("idkolone");
            entity.Property(e => e.Allownull)
                .HasColumnType("CHAR(3)")
                .HasColumnName("allownull");
            entity.Property(e => e.Datumupisa)
                .HasColumnType("DATE")
                .HasColumnName("datumupisa");
            entity.Property(e => e.Default)
                .HasColumnType("CHAR(50)")
                .HasColumnName("default");
            entity.Property(e => e.Fieldsize)
                .HasColumnType("CHAR(3)")
                .HasColumnName("fieldsize");
            entity.Property(e => e.Idtabele)
                .HasDefaultValueSql("0")
                .HasColumnName("idtabele");
            entity.Property(e => e.Indexed)
                .HasColumnType("CHAR(50)")
                .HasColumnName("indexed");
            entity.Property(e => e.Key)
                .HasColumnType("BOOLEAN")
                .HasColumnName("key");
            entity.Property(e => e.Korisnik)
                .HasColumnType("CHAR(50)")
                .HasColumnName("korisnik");
            entity.Property(e => e.Nazivkolone)
                .HasColumnType("CHAR(255)")
                .HasColumnName("nazivkolone");
            entity.Property(e => e.Skriven)
                .HasDefaultValueSql("0")
                .HasColumnType("BOOLEAN")
                .HasColumnName("skriven");
            entity.Property(e => e.Tippodatka)
                .HasColumnType("CHAR(50)")
                .HasColumnName("tippodatka");
            entity.Property(e => e.Verzija)
                .HasColumnType("CHAR(255)")
                .HasColumnName("verzija");
            entity.Property(e => e.Vremenskipecat)
                .HasDefaultValueSql("0")
                .HasColumnType("NUMERIC(15)")
                .HasColumnName("vremenskipecat");
        });

        modelBuilder.Entity<Kolonenove>(entity =>
        {
            entity.HasKey(e => e.Idkolone);

            entity.ToTable("kolonenove");

            entity.HasIndex(e => e.Idkolone, "kolonenoveidkolone172838").IsUnique();

            entity.HasIndex(e => e.Idtabele, "kolonenoveidtabele172838");

            entity.Property(e => e.Idkolone).HasColumnName("idkolone");
            entity.Property(e => e.Allownull)
                .HasColumnType("CHAR(3)")
                .HasColumnName("allownull");
            entity.Property(e => e.Datumupisa)
                .HasColumnType("DATE")
                .HasColumnName("datumupisa");
            entity.Property(e => e.Default)
                .HasColumnType("CHAR(50)")
                .HasColumnName("default");
            entity.Property(e => e.Fieldsize)
                .HasColumnType("CHAR(3)")
                .HasColumnName("fieldsize");
            entity.Property(e => e.Idtabele)
                .HasDefaultValueSql("0")
                .HasColumnName("idtabele");
            entity.Property(e => e.Indexed)
                .HasColumnType("CHAR(50)")
                .HasColumnName("indexed");
            entity.Property(e => e.Key)
                .HasColumnType("BOOLEAN")
                .HasColumnName("key");
            entity.Property(e => e.Korisnik)
                .HasColumnType("CHAR(50)")
                .HasColumnName("korisnik");
            entity.Property(e => e.Nazivkolone)
                .HasColumnType("CHAR(255)")
                .HasColumnName("nazivkolone");
            entity.Property(e => e.Skriven)
                .HasDefaultValueSql("0")
                .HasColumnType("BOOLEAN")
                .HasColumnName("skriven");
            entity.Property(e => e.Tippodatka)
                .HasColumnType("CHAR(50)")
                .HasColumnName("tippodatka");
            entity.Property(e => e.Verzija)
                .HasColumnType("CHAR(10)")
                .HasColumnName("verzija");
            entity.Property(e => e.Vremenskipecat)
                .HasDefaultValueSql("0")
                .HasColumnType("NUMERIC(15)")
                .HasColumnName("vremenskipecat");
        });

        modelBuilder.Entity<Parametri>(entity =>
        {
            entity.HasKey(e => e.Idparametra);

            entity.ToTable("parametri");

            entity.HasIndex(e => e.Idparametra, "parametriidparametra172838").IsUnique();

            entity.HasIndex(e => e.Idpoglavlja, "parametriidpoglavlja172838");

            entity.Property(e => e.Idparametra).HasColumnName("idparametra");
            entity.Property(e => e.Idpoglavlja)
                .HasDefaultValueSql("0")
                .HasColumnName("idpoglavlja");
            entity.Property(e => e.Nazivparametra)
                .HasColumnType("CHAR(255)")
                .HasColumnName("nazivparametra");
            entity.Property(e => e.Ocitano)
                .HasColumnType("CHAR(255)")
                .HasColumnName("ocitano");
            entity.Property(e => e.Podrazumijevano)
                .HasColumnType("CHAR(255)")
                .HasColumnName("podrazumijevano");
            entity.Property(e => e.Verzija)
                .HasColumnType("CHAR(10)")
                .HasColumnName("verzija");
        });

        modelBuilder.Entity<Poglavlja>(entity =>
        {
            entity.HasKey(e => e.Idpoglavlja);

            entity.ToTable("poglavlja");

            entity.HasIndex(e => e.Iddokumenta, "poglavljaiddokumenta172838");

            entity.HasIndex(e => e.Idpoglavlja, "poglavljaidpoglavlja172838").IsUnique();

            entity.Property(e => e.Idpoglavlja).HasColumnName("idpoglavlja");
            entity.Property(e => e.Datumupisa)
                .HasColumnType("DATE")
                .HasColumnName("datumupisa");
            entity.Property(e => e.Iddokumenta)
                .HasDefaultValueSql("0")
                .HasColumnName("iddokumenta");
            entity.Property(e => e.Korisnik)
                .HasColumnType("CHAR(50)")
                .HasColumnName("korisnik");
            entity.Property(e => e.Nazivpoglavlja)
                .HasColumnType("CHAR(50)")
                .HasColumnName("nazivpoglavlja");
            entity.Property(e => e.Postoji)
                .HasColumnType("BOOLEAN")
                .HasColumnName("postoji");
            entity.Property(e => e.Skriven)
                .HasDefaultValueSql("0")
                .HasColumnType("BOOLEAN")
                .HasColumnName("skriven");
            entity.Property(e => e.Verzija)
                .HasColumnType("CHAR(10)")
                .HasColumnName("verzija");
            entity.Property(e => e.Vremenskipecat)
                .HasDefaultValueSql("0")
                .HasColumnType("NUMERIC(15)")
                .HasColumnName("vremenskipecat");
        });

        modelBuilder.Entity<Programi>(entity =>
        {
            entity.HasKey(e => e.Idprograma);

            entity.ToTable("programi");

            entity.HasIndex(e => e.Idprograma, "programiidprograma172839").IsUnique();

            entity.Property(e => e.Idprograma).HasColumnName("idprograma");
            entity.Property(e => e.Datumupisa)
                .HasColumnType("DATE")
                .HasColumnName("datumupisa");
            entity.Property(e => e.Korisnik)
                .HasColumnType("CHAR(50)")
                .HasColumnName("korisnik");
            entity.Property(e => e.Nazivprograma)
                .HasColumnType("CHAR(255)")
                .HasColumnName("nazivprograma");
            entity.Property(e => e.Skriven)
                .HasDefaultValueSql("0")
                .HasColumnType("BOOLEAN")
                .HasColumnName("skriven");
            entity.Property(e => e.Verzija)
                .HasColumnType("CHAR(10)")
                .HasColumnName("verzija");
            entity.Property(e => e.Vremenskipecat)
                .HasDefaultValueSql("0")
                .HasColumnType("NUMERIC(15)")
                .HasColumnName("vremenskipecat");
        });

        modelBuilder.Entity<Promjenanazivatabela>(entity =>
        {
            entity.ToTable("promjenanazivatabela");

            entity.HasIndex(e => e.Id, "promjenanazivatabelaid172839").IsUnique();

            entity.HasIndex(e => e.Idprograma, "promjenanazivatabelaidprograma172839");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Datumupisa)
                .HasColumnType("DATE")
                .HasColumnName("datumupisa");
            entity.Property(e => e.Idprograma)
                .HasDefaultValueSql("0")
                .HasColumnName("idprograma");
            entity.Property(e => e.Korisnik)
                .HasColumnType("CHAR(50)")
                .HasColumnName("korisnik");
            entity.Property(e => e.Novinazivtabele)
                .HasColumnType("CHAR(50)")
                .HasColumnName("novinazivtabele");
            entity.Property(e => e.Skriven)
                .HasDefaultValueSql("0")
                .HasColumnType("BOOLEAN")
                .HasColumnName("skriven");
            entity.Property(e => e.Starinazivtabele)
                .HasColumnType("CHAR(50)")
                .HasColumnName("starinazivtabele");
            entity.Property(e => e.Verzija)
                .HasColumnType("CHAR(10)")
                .HasColumnName("verzija");
            entity.Property(e => e.Vremenskipecat)
                .HasDefaultValueSql("0")
                .HasColumnType("NUMERIC(15)")
                .HasColumnName("vremenskipecat");
        });

        modelBuilder.Entity<Putanje>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("putanje");

            entity.HasIndex(e => e.Idprograma, "putanjeidprograma172839");

            entity.Property(e => e.Idprograma)
                .HasDefaultValueSql("0")
                .HasColumnName("idprograma");
            entity.Property(e => e.Putanja1)
                .HasColumnType("CHAR(255)")
                .HasColumnName("putanja1");
            entity.Property(e => e.Putanja10)
                .HasColumnType("CHAR(255)")
                .HasColumnName("putanja10");
            entity.Property(e => e.Putanja11)
                .HasColumnType("CHAR(255)")
                .HasColumnName("putanja11");
            entity.Property(e => e.Putanja2)
                .HasColumnType("CHAR(255)")
                .HasColumnName("putanja2");
            entity.Property(e => e.Putanja3)
                .HasColumnType("CHAR(255)")
                .HasColumnName("putanja3");
            entity.Property(e => e.Putanja4)
                .HasColumnType("CHAR(255)")
                .HasColumnName("putanja4");
            entity.Property(e => e.Putanja5)
                .HasColumnType("CHAR(255)")
                .HasColumnName("putanja5");
            entity.Property(e => e.Putanja6)
                .HasColumnType("CHAR(255)")
                .HasColumnName("putanja6");
            entity.Property(e => e.Putanja7)
                .HasColumnType("CHAR(255)")
                .HasColumnName("putanja7");
            entity.Property(e => e.Putanja8)
                .HasColumnType("CHAR(255)")
                .HasColumnName("putanja8");
            entity.Property(e => e.Putanja9)
                .HasColumnType("CHAR(255)")
                .HasColumnName("putanja9");
            entity.Property(e => e.VerzijaBe)
                .HasColumnType("CHAR(10)")
                .HasColumnName("verzija_be");
            entity.Property(e => e.Verzijamatrix)
                .HasColumnType("CHAR(10)")
                .HasColumnName("verzijamatrix");
        });

        modelBuilder.Entity<Relacije>(entity =>
        {
            entity.HasKey(e => e.Idrelacije);

            entity.ToTable("relacije");

            entity.HasIndex(e => e.Idprograma, "relacijeidprograma172839");

            entity.HasIndex(e => e.Idrelacije, "relacijeidrelacije172839").IsUnique();

            entity.Property(e => e.Idrelacije).HasColumnName("idrelacije");
            entity.Property(e => e.Datumupisa)
                .HasColumnType("DATE")
                .HasColumnName("datumupisa");
            entity.Property(e => e.Idprograma)
                .HasDefaultValueSql("0")
                .HasColumnName("idprograma");
            entity.Property(e => e.Korisnik)
                .HasColumnType("CHAR(50)")
                .HasColumnName("korisnik");
            entity.Property(e => e.Nazivrelacije)
                .HasColumnType("CHAR(100)")
                .HasColumnName("nazivrelacije");
            entity.Property(e => e.Polje)
                .HasColumnType("CHAR(50)")
                .HasColumnName("polje");
            entity.Property(e => e.Skriven)
                .HasDefaultValueSql("0")
                .HasColumnType("BOOLEAN")
                .HasColumnName("skriven");
            entity.Property(e => e.Tabelad)
                .HasColumnType("CHAR(50)")
                .HasColumnName("tabelad");
            entity.Property(e => e.Tabelal)
                .HasColumnType("CHAR(50)")
                .HasColumnName("tabelal");
            entity.Property(e => e.Updatedeletecascade)
                .HasColumnType("BOOLEAN")
                .HasColumnName("updatedeletecascade");
            entity.Property(e => e.Verzija)
                .HasColumnType("CHAR(10)")
                .HasColumnName("verzija");
            entity.Property(e => e.Vremenskipecat)
                .HasDefaultValueSql("0")
                .HasColumnType("NUMERIC(15)")
                .HasColumnName("vremenskipecat");
        });

        modelBuilder.Entity<Rjecnik>(entity =>
        {
            entity.HasKey(e => e.Idrjecnik);

            entity.ToTable("rjecnik");

            entity.HasIndex(e => e.Idjezik, "rjecnikidjezik172839");

            entity.HasIndex(e => e.Idrjecnik, "rjecnikidrjecnik172839").IsUnique();

            entity.HasIndex(e => e.Original, "rjecnikoriginal172839");

            entity.Property(e => e.Idrjecnik).HasColumnName("idrjecnik");
            entity.Property(e => e.Datumupisa)
                .HasColumnType("DATE")
                .HasColumnName("datumupisa");
            entity.Property(e => e.Idjezik)
                .HasDefaultValueSql("0")
                .HasColumnName("idjezik");
            entity.Property(e => e.Korisnik)
                .HasColumnType("CHAR(50)")
                .HasColumnName("korisnik");
            entity.Property(e => e.Original)
                .HasColumnType("CHAR(255)")
                .HasColumnName("original");
            entity.Property(e => e.Prijevod)
                .HasColumnType("CHAR(255)")
                .HasColumnName("prijevod");
            entity.Property(e => e.Skriven)
                .HasDefaultValueSql("0")
                .HasColumnType("BOOLEAN")
                .HasColumnName("skriven");
            entity.Property(e => e.Vremenskipecat)
                .HasDefaultValueSql("0")
                .HasColumnType("NUMERIC(15)")
                .HasColumnName("vremenskipecat");
        });

        modelBuilder.Entity<Tabele>(entity =>
        {
            entity.HasKey(e => e.Idtabele);

            entity.ToTable("tabele");

            entity.HasIndex(e => e.Idprograma, "tabeleidprograma172839");

            entity.HasIndex(e => e.Idtabele, "tabeleidtabele172839").IsUnique();

            entity.HasIndex(e => e.Sid, "tabelesid172839");

            entity.Property(e => e.Idtabele).HasColumnName("idtabele");
            entity.Property(e => e.Datumupisa)
                .HasColumnType("DATE")
                .HasColumnName("datumupisa");
            entity.Property(e => e.Idprograma)
                .HasDefaultValueSql("0")
                .HasColumnName("idprograma");
            entity.Property(e => e.Korisnik)
                .HasColumnType("CHAR(50)")
                .HasColumnName("korisnik");
            entity.Property(e => e.Nazivtabele)
                .HasColumnType("CHAR(255)")
                .HasColumnName("nazivtabele");
            entity.Property(e => e.Sid)
                .HasDefaultValueSql("0")
                .HasColumnName("sid");
            entity.Property(e => e.Skriven)
                .HasDefaultValueSql("0")
                .HasColumnType("BOOLEAN")
                .HasColumnName("skriven");
            entity.Property(e => e.Verzija)
                .HasColumnType("CHAR(10)")
                .HasColumnName("verzija");
            entity.Property(e => e.Vremenskipecat)
                .HasDefaultValueSql("0")
                .HasColumnType("NUMERIC(15)")
                .HasColumnName("vremenskipecat");
        });

        modelBuilder.Entity<Tabelenove>(entity =>
        {
            entity.HasKey(e => e.Idtabele);

            entity.ToTable("tabelenove");

            entity.HasIndex(e => e.Idprograma, "tabelenoveidprograma172839");

            entity.HasIndex(e => e.Idtabele, "tabelenoveidtabele172839").IsUnique();

            entity.Property(e => e.Idtabele).HasColumnName("idtabele");
            entity.Property(e => e.Cijelatabela)
                .HasColumnType("BOOLEAN")
                .HasColumnName("cijelatabela");
            entity.Property(e => e.Datumupisa)
                .HasColumnType("DATE")
                .HasColumnName("datumupisa");
            entity.Property(e => e.Idprograma)
                .HasDefaultValueSql("0")
                .HasColumnName("idprograma");
            entity.Property(e => e.Korisnik)
                .HasColumnType("CHAR(50)")
                .HasColumnName("korisnik");
            entity.Property(e => e.Nazivtabele)
                .HasColumnType("CHAR(255)")
                .HasColumnName("nazivtabele");
            entity.Property(e => e.Skriven)
                .HasDefaultValueSql("0")
                .HasColumnType("BOOLEAN")
                .HasColumnName("skriven");
            entity.Property(e => e.Verzija)
                .HasColumnType("CHAR(10)")
                .HasColumnName("verzija");
            entity.Property(e => e.Vremenskipecat)
                .HasDefaultValueSql("0")
                .HasColumnType("NUMERIC(15)")
                .HasColumnName("vremenskipecat");
        });

        modelBuilder.Entity<Tempkolonet1>(entity =>
        {
            entity.HasKey(e => e.Idkolone);

            entity.ToTable("tempkolonet1");

            entity.Property(e => e.Idkolone).HasColumnName("idkolone");
            entity.Property(e => e.Allownull)
                .HasColumnType("CHAR(3)")
                .HasColumnName("allownull");
            entity.Property(e => e.Datumupisa)
                .HasColumnType("DATE")
                .HasColumnName("datumupisa");
            entity.Property(e => e.Default)
                .HasColumnType("CHAR(50)")
                .HasColumnName("default");
            entity.Property(e => e.Fieldsize)
                .HasColumnType("CHAR(3)")
                .HasColumnName("fieldsize");
            entity.Property(e => e.Idtabele).HasColumnName("idtabele");
            entity.Property(e => e.Indexed)
                .HasColumnType("CHAR(50)")
                .HasColumnName("indexed");
            entity.Property(e => e.Key)
                .HasColumnType("BOOLEAN")
                .HasColumnName("key");
            entity.Property(e => e.Korisnik)
                .HasColumnType("CHAR(50)")
                .HasColumnName("korisnik");
            entity.Property(e => e.Nazivkolone)
                .HasColumnType("CHAR(255)")
                .HasColumnName("nazivkolone");
            entity.Property(e => e.Skriven)
                .HasDefaultValueSql("0")
                .HasColumnType("BOOLEAN")
                .HasColumnName("skriven");
            entity.Property(e => e.Tippodatka)
                .HasColumnType("CHAR(50)")
                .HasColumnName("tippodatka");
            entity.Property(e => e.Verzija)
                .HasColumnType("CHAR(10)")
                .HasColumnName("verzija");
            entity.Property(e => e.Vremenskipecat)
                .HasDefaultValueSql("0")
                .HasColumnType("NUMERIC(15)")
                .HasColumnName("vremenskipecat");
        });

        modelBuilder.Entity<Tempkolonet2>(entity =>
        {
            entity.HasKey(e => e.Idkolone);

            entity.ToTable("tempkolonet2");

            entity.Property(e => e.Idkolone).HasColumnName("idkolone");
            entity.Property(e => e.Allownull)
                .HasColumnType("CHAR(3)")
                .HasColumnName("allownull");
            entity.Property(e => e.Datumupisa)
                .HasColumnType("DATE")
                .HasColumnName("datumupisa");
            entity.Property(e => e.Default)
                .HasColumnType("CHAR(50)")
                .HasColumnName("default");
            entity.Property(e => e.Fieldsize)
                .HasColumnType("CHAR(3)")
                .HasColumnName("fieldsize");
            entity.Property(e => e.Idtabele).HasColumnName("idtabele");
            entity.Property(e => e.Indexed)
                .HasColumnType("CHAR(50)")
                .HasColumnName("indexed");
            entity.Property(e => e.Key)
                .HasColumnType("BOOLEAN")
                .HasColumnName("key");
            entity.Property(e => e.Korisnik)
                .HasColumnType("CHAR(50)")
                .HasColumnName("korisnik");
            entity.Property(e => e.Nazivkolone)
                .HasColumnType("CHAR(255)")
                .HasColumnName("nazivkolone");
            entity.Property(e => e.Skriven)
                .HasDefaultValueSql("0")
                .HasColumnType("BOOLEAN")
                .HasColumnName("skriven");
            entity.Property(e => e.Tippodatka)
                .HasColumnType("CHAR(50)")
                .HasColumnName("tippodatka");
            entity.Property(e => e.Verzija)
                .HasColumnType("CHAR(10)")
                .HasColumnName("verzija");
            entity.Property(e => e.Vremenskipecat)
                .HasDefaultValueSql("0")
                .HasColumnType("NUMERIC(15)")
                .HasColumnName("vremenskipecat");
        });

        modelBuilder.Entity<Tippodatka>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tippodatka");

            entity.Property(e => e.Datumupisa)
                .HasColumnType("DATE")
                .HasColumnName("datumupisa");
            entity.Property(e => e.Korisnik)
                .HasColumnType("CHAR(50)")
                .HasColumnName("korisnik");
            entity.Property(e => e.Skriven)
                .HasDefaultValueSql("0")
                .HasColumnType("BOOLEAN")
                .HasColumnName("skriven");
            entity.Property(e => e.Tippodatka1)
                .HasColumnType("CHAR(50)")
                .HasColumnName("tippodatka");
            entity.Property(e => e.Vremenskipecat)
                .HasDefaultValueSql("0")
                .HasColumnType("NUMERIC(15)")
                .HasColumnName("vremenskipecat");
        });

        modelBuilder.Entity<Log>(entity =>
        {
            entity.HasKey(e => e.Idlog);
            entity.ToTable("log");

            entity.Property(e => e.Idlog).HasColumnName("idlog");
            entity.Property(e => e.Datumvrijeme)
                .HasColumnType("DATE")
                .HasColumnName("datumvrijeme");
            entity.Property(e => e.Nivo)
                .HasColumnType("CHAR(255)")
                .HasColumnName("nivo");
            entity.Property(e => e.Kategorija)
                .HasColumnType("CHAR(255)")
                .HasColumnName("kategorija");
            entity.Property(e => e.Poruka)
                .HasColumnType("CHAR(255)")
                .HasColumnName("poruka");
            entity.Property(e => e.Detalji)
                .HasColumnType("CHAR(500)")
                .HasColumnName("detalji");
            entity.Property(e => e.Sqlkod)
                .HasColumnType("CHAR(500)")
                .HasColumnName("sqlkod");
            entity.Property(e => e.Backend)
                .HasColumnType("CHAR(255)")
                .HasColumnName("backend");
            entity.Property(e => e.Idprogram)
                .HasColumnName("idprogram");
            entity.Property(e => e.Korisnik)
                .HasColumnType("CHAR(50)")
                .HasColumnName("korisnik");
            entity.Property(e => e.Masina)
                .HasColumnType("CHAR(255)")
                .HasColumnName("masina");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
