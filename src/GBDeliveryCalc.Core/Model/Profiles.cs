namespace GBDeliveryCalc.Core.Model;

/// <summary>A configured installation team (settings record, editable).</summary>
public sealed record TeamProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>Loaded labor cost per hour for the whole team (wages plus employer costs). Null while not yet set up.</summary>
    public decimal? LoadedLaborCostPerHour { get; init; }

    public Guid? DefaultVehicleId { get; init; }
    public bool IsArchived { get; init; }

    public TeamSnapshot ToSnapshot() => new()
    {
        TeamId = Id,
        Name = Name,
        Description = Description,
        LoadedLaborCostPerHour = LoadedLaborCostPerHour,
        WasArchived = IsArchived,
    };
}

/// <summary>A configured vehicle (settings record, editable).</summary>
public sealed record VehicleProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public FuelType FuelType { get; init; } = FuelType.RegularUnleaded;
    public decimal? EstimatedMpg { get; init; }

    /// <summary>Internal operating cost per mile excluding fuel (maintenance, tires, insurance, depreciation/lease).</summary>
    public decimal? OperatingCostPerMileExcludingFuel { get; init; }

    public bool IsArchived { get; init; }

    public VehicleSnapshot ToSnapshot() => new()
    {
        VehicleId = Id,
        Name = Name,
        FuelType = FuelType,
        EstimatedMpg = EstimatedMpg,
        OperatingCostPerMileExcludingFuel = OperatingCostPerMileExcludingFuel,
        WasArchived = IsArchived,
    };
}

/// <summary>Frozen copy of a team at the time an estimate was saved.</summary>
public sealed record TeamSnapshot
{
    public Guid TeamId { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public decimal? LoadedLaborCostPerHour { get; init; }
    public bool WasArchived { get; init; }
}

/// <summary>Frozen copy of a vehicle at the time an estimate was saved.</summary>
public sealed record VehicleSnapshot
{
    public Guid VehicleId { get; init; }
    public string Name { get; init; } = "";
    public FuelType FuelType { get; init; }
    public decimal? EstimatedMpg { get; init; }
    public decimal? OperatingCostPerMileExcludingFuel { get; init; }
    public bool WasArchived { get; init; }
}

/// <summary>Company-wide settings.</summary>
public sealed record CompanySettings
{
    public const int FactoryDefaultInstallMinutes = 180;

    /// <summary>Default starting address (the shop). Blank until staff enter the real street address.</summary>
    public string OriginAddress { get; init; } = "";

    public int DefaultInstallMinutes { get; init; } = FactoryDefaultInstallMinutes;

    /// <summary>Round partial travel and installation hours up to the next whole hour for costing (default off).</summary>
    public bool RoundLaborHoursUp { get; init; }
}

/// <summary>An additional direct job cost (parking, disposal fee, permit, helper...).</summary>
public sealed record DirectCost
{
    public string Description { get; init; } = "";
    public decimal Amount { get; init; }
}
