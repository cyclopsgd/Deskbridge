using Deskbridge.Core.Models;

namespace Deskbridge.Core.Services;

/// <summary>
/// Generates deterministic enterprise-realistic connection datasets for benchmarks and tests.
/// Same (connectionCount, seed) pair always produces identical output.
/// </summary>
public static class TestDataGenerator
{
    private static readonly string[] RegionNames = ["useast", "uswest", "euwest", "eucentral", "apsouth"];
    private static readonly string[] EnvironmentNames = ["prod", "staging", "dev", "qa"];
    private static readonly string[] RoleNames = ["web", "api", "db", "cache", "worker"];
    private static readonly string[] TagPool = ["production", "staging", "monitored", "critical", "legacy", "windows-server"];

    public static (IReadOnlyList<ConnectionModel> Connections, IReadOnlyList<ConnectionGroup> Groups)
        Generate(int connectionCount, int seed = 42)
    {
        // Fold connectionCount into seed so N=100 is NOT a subset of N=1000
        var rng = new Random(HashCode.Combine(seed, connectionCount));

        // Target approximately connectionCount / 10 total groups across all levels
        int targetTotalGroups = Math.Max(6, connectionCount / 10);

        // Calculate level counts to hit the target
        // Total groups = regions + (regions * envsPerRegion) + (regions * envsPerRegion * rolesPerEnv)
        // We solve for counts that give us approximately the target
        var (regionCount, envsPerRegion, rolesPerEnv) = ComputeLevelCounts(targetTotalGroups, rng);

        var groups = new List<ConnectionGroup>();
        var leafGroups = new List<(Guid Id, string Region, string Env, string Role)>();

        int groupSortOrder = 0;

        // Level 1: Regions
        var regions = new List<(Guid Id, string Name)>();
        for (int r = 0; r < regionCount; r++)
        {
            var regionId = DeterministicGuid(rng);
            string regionName = RegionNames[r % RegionNames.Length];
            if (r >= RegionNames.Length)
                regionName += (r / RegionNames.Length + 1).ToString();

            groups.Add(new ConnectionGroup
            {
                Id = regionId,
                Name = regionName,
                ParentGroupId = null,
                SortOrder = groupSortOrder++
            });
            regions.Add((regionId, regionName));
        }

        // Level 2: Environments per region
        var environments = new List<(Guid Id, string Region, string EnvName, Guid RegionId)>();
        foreach (var (regionId, regionName) in regions)
        {
            int envCount = envsPerRegion;

            for (int e = 0; e < envCount; e++)
            {
                var envId = DeterministicGuid(rng);
                string envName = EnvironmentNames[e % EnvironmentNames.Length];

                groups.Add(new ConnectionGroup
                {
                    Id = envId,
                    Name = envName,
                    ParentGroupId = regionId,
                    SortOrder = groupSortOrder++
                });
                environments.Add((envId, regionName, envName, regionId));
            }
        }

        // Level 3: Roles per environment
        foreach (var (envId, regionName, envName, _) in environments)
        {
            int roleCount = rolesPerEnv;

            for (int ro = 0; ro < roleCount; ro++)
            {
                var roleId = DeterministicGuid(rng);
                string roleName = RoleNames[ro % RoleNames.Length];

                groups.Add(new ConnectionGroup
                {
                    Id = roleId,
                    Name = roleName,
                    ParentGroupId = envId,
                    SortOrder = groupSortOrder++
                });
                leafGroups.Add((roleId, regionName, envName, roleName));
            }
        }

        // Distribute connections across leaf groups with uneven weighting
        var weights = new int[leafGroups.Count];
        int totalWeight = 0;
        for (int i = 0; i < leafGroups.Count; i++)
        {
            weights[i] = rng.Next(1, 10);
            totalWeight += weights[i];
        }

        // Allocate connections proportionally, ensuring at least 1 per leaf
        var allocations = new int[leafGroups.Count];
        int allocated = 0;
        for (int i = 0; i < leafGroups.Count; i++)
        {
            allocations[i] = Math.Max(1, (int)((long)connectionCount * weights[i] / totalWeight));
            allocated += allocations[i];
        }

        // Distribute remainder or surplus
        int diff = connectionCount - allocated;
        if (diff > 0)
        {
            // Add remaining connections round-robin starting from highest-weighted groups
            for (int i = 0; diff > 0; i = (i + 1) % leafGroups.Count)
            {
                allocations[i]++;
                diff--;
            }
        }
        else if (diff < 0)
        {
            // Remove excess from groups with more than 1 connection
            for (int i = leafGroups.Count - 1; diff < 0; i--)
            {
                if (i < 0) i = leafGroups.Count - 1;
                if (allocations[i] > 1)
                {
                    allocations[i]--;
                    diff++;
                }
            }
        }

        // Generate connections
        var connections = new List<ConnectionModel>(connectionCount);
        var baseDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (int leafIdx = 0; leafIdx < leafGroups.Count; leafIdx++)
        {
            var (groupId, region, env, role) = leafGroups[leafIdx];
            int count = allocations[leafIdx];

            for (int n = 0; n < count; n++)
            {
                string hostname = $"srv-{region}-{env}-{role}-{(n + 1):D3}";
                string name = hostname;

                bool useOwn = rng.Next(100) < 20; // 80/20 Inherit/Own split
                string username = (connections.Count % 2 == 0) ? $"admin-{region}" : $"svc-{role}";
                string domain = (connections.Count % 2 == 0) ? "corp.example.com" : $"{region}.internal";

                // Tags: 0-2 from pool
                int tagCount = rng.Next(3);
                var tags = new List<string>();
                for (int t = 0; t < tagCount; t++)
                {
                    string tag = TagPool[rng.Next(TagPool.Length)];
                    if (!tags.Contains(tag))
                        tags.Add(tag);
                }

                // Notes: ~10% have notes
                string? notes = rng.Next(10) == 0
                    ? $"Auto-provisioned {env} server in {region}"
                    : null;

                var createdAt = baseDate.AddDays(rng.Next(0, 365));
                var updatedAt = createdAt.AddDays(rng.Next(0, 30));

                connections.Add(new ConnectionModel
                {
                    Id = DeterministicGuid(rng),
                    Name = name,
                    Hostname = hostname,
                    Port = 3389,
                    Protocol = Protocol.Rdp,
                    Username = username,
                    Domain = domain,
                    GroupId = groupId,
                    SortOrder = n,
                    Notes = notes,
                    Tags = tags,
                    CredentialMode = useOwn ? CredentialMode.Own : CredentialMode.Inherit,
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt,
                    LastUsedAt = null,
                    EnableCredSspSupport = true,
                    AuthenticationLevel = 0
                });
            }
        }

        return (connections, groups);
    }

    private static Guid DeterministicGuid(Random rng)
    {
        var bytes = new byte[16];
        rng.NextBytes(bytes);
        return new Guid(bytes);
    }

    /// <summary>
    /// Computes region, environment, and role counts per level to achieve approximately
    /// the target total group count. Total = R + R*E + R*E*Ro where R=regions, E=envs, Ro=roles.
    /// </summary>
    private static (int Regions, int EnvsPerRegion, int RolesPerEnv) ComputeLevelCounts(
        int targetTotalGroups, Random rng)
    {
        // We want total groups = R + R*E + R*E*Ro ~ targetTotalGroups
        // Leaf groups = R * E * Ro, and total = leaf + R*E + R = leaf * (1 + 1/Ro + 1/(E*Ro))
        // Approximate: leaf ~ targetTotalGroups * Ro / (Ro + 1 + 1/E)
        // Iterate a few candidate combos and pick the one closest to target

        int bestR = 3, bestE = 2, bestRo = 1;
        int bestDiff = int.MaxValue;

        for (int r = 3; r <= Math.Min(5, RegionNames.Length); r++)
        {
            for (int e = 1; e <= Math.Min(4, EnvironmentNames.Length); e++)
            {
                for (int ro = 1; ro <= Math.Min(3, RoleNames.Length); ro++)
                {
                    int total = r + r * e + r * e * ro;
                    int diff = Math.Abs(total - targetTotalGroups);
                    if (diff < bestDiff)
                    {
                        bestDiff = diff;
                        bestR = r;
                        bestE = e;
                        bestRo = ro;
                    }
                }
            }
        }

        return (bestR, bestE, bestRo);
    }
}
