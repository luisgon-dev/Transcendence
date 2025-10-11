namespace Transcendence.Data.Models.LoL.Match;

public class Runes
{
    public Guid Id { get; set; }

    public int PrimaryStyle { get; set; }
    public int SubStyle { get; set; }

    // primary style
    public int Perk0 { get; set; }
    public int Perk1 { get; set; }
    public int Perk2 { get; set; }
    public int Perk3 { get; set; }

    // substyle
    public int Perk4 { get; set; }
    public int Perk5 { get; set; }

    // rune vars
    public int[] RuneVars0 { get; set; } = new int[3];
    public int[] RuneVars1 { get; set; } = new int[3];
    public int[] RuneVars2 { get; set; } = new int[3];
    public int[] RuneVars3 { get; set; } = new int[3];
    public int[] RuneVars4 { get; set; } = new int[3];
    public int[] RuneVars5 { get; set; } = new int[3];

    // stats
    public int StatDefense { get; set; }
    public int StatFlex { get; set; }
    public int StatOffense { get; set; }
}
public class RuneSelection
{
}
