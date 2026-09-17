using KCAS.Admin.Data;
using Microsoft.Extensions.Configuration;

namespace KCAS.Admin.Tests;

public sealed class CompliancePresentationTests
{
    [Fact]
    public void Review_schedule_is_one_year_from_last_update()
    {
        var updated = new DateTime(2026, 9, 17, 10, 30, 0, DateTimeKind.Utc);
        var bra = new BusinessRiskAssessment { UpdatedAtUtc = updated };
        var rmcp = new RmcpVersion { UpdatedAtUtc = updated };

        Assert.Equal(new DateOnly(2027, 9, 17), ComplianceReviewSchedule.BraDueDate(bra));
        Assert.Equal(new DateOnly(2027, 9, 17), ComplianceReviewSchedule.RmcpDueDate(rmcp));
        Assert.Equal("Due soon", ComplianceReviewSchedule.Status(new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 17)));
    }

    [Fact]
    public void Live_server_drive_is_presented_as_workstation_drive()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DocumentPaths:ServerDrive"] = "E:",
            ["DocumentPaths:WorkstationDrive"] = "Z:"
        }).Build();
        var service = new DocumentPathDisplayService(configuration);

        Assert.Equal(@"Z:\Userdata\Kanaan Trust\Clients\Client A", service.ForWorkstation(@"E:\Userdata\Kanaan Trust\Clients\Client A"));
        Assert.Equal(@"C:\Download\_kanaan\Client A", service.ForWorkstation(@"C:\Download\_kanaan\Client A"));
    }
}
