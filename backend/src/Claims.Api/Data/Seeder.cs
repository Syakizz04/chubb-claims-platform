using Claims.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Data;

/// <summary>
/// Seeds reference data (markets, dev users) and a realistic spread of demo claims (N-1).
/// Demo claims go through the real domain methods and the repository, so they produce real
/// events and outbox rows. Insights builds its dashboards from them, exactly as in production.
/// </summary>
public class Seeder(ClaimsDbContext db, ClaimRepository repo, TimeProvider clock, ILogger<Seeder> log)
{
    public static readonly Market[] Markets =
    [
        new() { Code = "KR", Name = "South Korea", CurrencyCode = "KRW", MinorUnits = 0 },
        new() { Code = "HK", Name = "Hong Kong", CurrencyCode = "HKD", MinorUnits = 2 },
        new() { Code = "TW", Name = "Taiwan", CurrencyCode = "TWD", MinorUnits = 2 },
        new() { Code = "TH", Name = "Thailand", CurrencyCode = "THB", MinorUnits = 2 },
        new() { Code = "MY", Name = "Malaysia", CurrencyCode = "MYR", MinorUnits = 2 },
        new() { Code = "SG", Name = "Singapore", CurrencyCode = "SGD", MinorUnits = 2 },
    ];

    public static readonly User[] Users =
    [
        new() { Id = "claimant-sg", DisplayName = "Mei Ling Tan", Role = UserRole.Claimant },
        new() { Id = "claimant-kr", DisplayName = "Kim Min-jun", Role = UserRole.Claimant },
        new() { Id = "claimant-hk", DisplayName = "Chan Wing Yee", Role = UserRole.Claimant },
        new() { Id = "claimant-tw", DisplayName = "Lin Yu-ting", Role = UserRole.Claimant },
        new() { Id = "claimant-th", DisplayName = "Somchai Prasert", Role = UserRole.Claimant },
        new() { Id = "claimant-my", DisplayName = "Nur Aisyah Rahman", Role = UserRole.Claimant },
        new() { Id = "handler-1", DisplayName = "Priya Nair", Role = UserRole.Handler },
        new() { Id = "handler-2", DisplayName = "Daniel Wong", Role = UserRole.Handler },
        new() { Id = "handler-3", DisplayName = "Siti Rahman", Role = UserRole.Handler },
        new() { Id = "manager-1", DisplayName = "Grace Lim", Role = UserRole.Manager },
    ];

    public async Task SeedAsync(bool includeDemoClaims, CancellationToken ct = default)
    {
        if (!await db.Markets.AnyAsync(ct))
        {
            db.Markets.AddRange(Markets);
            db.MarketSequences.AddRange(Markets.Select(m => new MarketSequence { MarketCode = m.Code }));
            db.Users.AddRange(Users);
            await db.SaveChangesAsync(ct);
            log.LogInformation("Seeded {Markets} markets and {Users} users", Markets.Length, Users.Length);
        }

        if (includeDemoClaims && !await db.Claims.AnyAsync(ct))
        {
            await SeedDemoClaimsAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    private async Task SeedDemoClaimsAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        User U(string id) => Users.Single(u => u.Id == id);
        DateTimeOffset Ago(double days) => now.AddDays(-days);

        async Task<Claim> Submit(string market, string claimant, ClaimType type, decimal amount, double daysAgo, string description, string location)
        {
            var m = Markets.Single(x => x.Code == market);
            var reference = await repo.NextReferenceAsync(market, ct);
            var incident = DateOnly.FromDateTime(Ago(daysAgo + 1).UtcDateTime);
            var claim = Claim.Submit(reference, m, claimant, new Claim.NewClaim(type, $"POL-{market}-{Random.Shared.Next(100000, 999999)}",
                incident, location, description, amount,
                type == ClaimType.Motor ? $"{market}{Random.Shared.Next(1000, 9999)}" : null,
                type == ClaimType.Property ? location : null), Ago(daysAgo));
            repo.Add(claim);
            await repo.SaveAsync(claim, ct);
            return claim;
        }

        async Task Step(Claim c, Action<Claim> action)
        {
            action(c);
            await repo.SaveAsync(c, ct);
        }

        // Queue: unassigned, waiting for pick-up.
        await Submit("SG", "claimant-sg", ClaimType.Motor, 4_200.00m, 2, "Rear-ended at a traffic light on Orchard Road; bumper and boot damaged.", "Orchard Road, Singapore");
        await Submit("KR", "claimant-kr", ClaimType.Motor, 3_500_000m, 1, "Side-swiped while parked; driver door and mirror need replacing.", "Gangnam-gu, Seoul");
        await Submit("HK", "claimant-hk", ClaimType.Property, 28_000.00m, 5, "Burst pipe in the flat above flooded the kitchen and damaged cabinets.", "Tai Koo Shing, Hong Kong");
        await Submit("SG", "claimant-sg", ClaimType.Motor, 2_800.00m, 0.2, "Windscreen cracked by debris from a lorry on the PIE.", "Pan-Island Expressway, Singapore");

        // In progress with handlers.
        var my = await Submit("MY", "claimant-my", ClaimType.Motor, 9_800.00m, 12, "Collision at a roundabout; front-left wheel and suspension damaged.", "Petaling Jaya, Selangor");
        await Step(my, c => c.PickUp(U("handler-1"), Ago(11)));
        await Step(my, c => c.AdjustReserve("handler-1", 8_500.00m, "Workshop quote received", Ago(9)));

        var th = await Submit("TH", "claimant-th", ClaimType.Property, 120_000.00m, 20, "Storm damage to roof tiles and water ingress into the bedroom.", "Chatuchak, Bangkok");
        await Step(th, c => c.PickUp(U("handler-1"), Ago(18)));
        await Step(th, c => c.RequestInformation("handler-1", "Please provide photos of the roof damage and the contractor's quote.", Ago(15)));

        var tw = await Submit("TW", "claimant-tw", ClaimType.Motor, 95_000.00m, 9, "Scooter collided with my car at a junction; rear quarter panel dented.", "Da'an District, Taipei");
        await Step(tw, c => c.PickUp(U("handler-2"), Ago(8)));

        var thOld = await Submit("TH", "claimant-th", ClaimType.Motor, 45_000.00m, 45, "Hit a pothole at speed; two tyres and a rim damaged.", "Sukhumvit Road, Bangkok");
        await Step(thOld, c => c.PickUp(U("handler-2"), Ago(44)));
        await Step(thOld, c => c.RequestInformation("handler-2", "Please send the police report reference for the incident.", Ago(40)));

        var myProp = await Submit("MY", "claimant-my", ClaimType.Property, 22_000.00m, 3, "Break-in at home; laptop, jewellery and a television were stolen.", "Mont Kiara, Kuala Lumpur");
        await Step(myProp, c => c.PickUp(U("handler-3"), Ago(2)));

        // Decided.
        var sg = await Submit("SG", "claimant-sg", ClaimType.Property, 18_500.00m, 35, "Kitchen fire damaged the hob, cabinets and ceiling.", "Tampines, Singapore");
        await Step(sg, c => c.PickUp(U("handler-2"), Ago(34)));
        await Step(sg, c => c.Approve("handler-2", 17_000.00m, "Approved per loss adjuster report", Ago(10)));

        var kr = await Submit("KR", "claimant-kr", ClaimType.Property, 12_000_000m, 40, "Typhoon damage to balcony windows and flooring.", "Haeundae-gu, Busan");
        await Step(kr, c => c.PickUp(U("handler-3"), Ago(39)));
        await Step(kr, c => c.Approve("handler-3", 11_000_000m, "Excess deducted", Ago(25)));
        await Step(kr, c => c.Settle("handler-3", Ago(20)));

        var hk = await Submit("HK", "claimant-hk", ClaimType.Motor, 15_000.00m, 25, "Minor scrape in a car park; paintwork on rear bumper.", "Causeway Bay, Hong Kong");
        await Step(hk, c => c.PickUp(U("handler-3"), Ago(24)));
        await Step(hk, c => c.Reject("handler-3", "Damage pre-dates the policy start date per inspection report.", Ago(18)));

        log.LogInformation("Seeded demo claims");
    }
}
