using Microsoft.EntityFrameworkCore;
using ProjectSyndraBackend.Data.Models;

namespace ProjectSyndraBackend.Data;

public class ProjectSyndraContext : DbContext
{
    public DbSet<Summoner> Summoners { get; set; }
    
    
    
}