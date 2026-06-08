using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260608100000_AddClassroomEmpiricalDifficultyScoring")]
    partial class AddClassroomEmpiricalDifficultyScoring
    {
    }
}
