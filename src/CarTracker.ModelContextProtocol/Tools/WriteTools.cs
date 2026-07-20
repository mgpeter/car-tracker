using System.ComponentModel;
using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.Domain.Expenses;
using CarTracker.Domain.Logs;
using CarTracker.Domain.Vehicles;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using CarTracker.Shared.Metrics;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CarTracker.ModelContextProtocol.Tools;

/// <summary>
/// The write tools (read-write scope): add/log entries and the safe updates (odometer, mark-check-done,
/// complete-task). Every one stamps <see cref="EntrySource.Mcp"/> and runs through the same factory or service
/// the web write uses, so an MCP-logged fill is indistinguishable from a typed one bar its provenance. Nothing
/// here edits or deletes an existing row.
/// </summary>
[McpServerToolType]
[Authorize(Policy = "McpWrite")]
public sealed class WriteTools
{
    private const EntrySource Source = EntrySource.Mcp;

    // ---- factory-backed --------------------------------------------------------------------------------

    [McpServerTool(Name = "log_fuel_fillup")]
    [Description(
        "Record a fuel fill-up. Writes the fill, its odometer reading and its mirrored expense in one transaction, "
        + "then returns the computed MPG. Litres are the sole basis of MPG. fillLevel Full/unrecorded closes the "
        + "tank and measures the segment; Half/Quarter defer MPG to the next full fill. A mileage below the current "
        + "odometer is flagged, never rejected. Example: date 2026-07-20, mileage 80900, litres 47.2, pricePerLitre 1.45.")]
    public static async Task<McpResult<FuelFillResult>> LogFuelFillup(
        VehicleResolver resolver,
        FuelEntryFactory factory,
        AnomalyScanner scanner,
        IDerivedMetricsService metrics,
        [Description("Date of the fill (yyyy-MM-dd).")] DateOnly date,
        [Description("Odometer at the fill.")] int mileage,
        [Description("Litres pumped — the basis of MPG.")] decimal litres,
        [Description("Price per litre in £.")] decimal pricePerLitre,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Receipt total in £. Omit to compute litres × price.")] decimal? totalCost = null,
        [Description("Filling station.")] string? station = null,
        [Description("Full, Half or Quarter. Omit to treat as a full fill.")] FillLevel? fillLevel = null,
        [Description("Free-text note.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);

        if (litres <= 0) throw new McpException("A fill must have litres — they are the sole basis of MPG.");
        if (pricePerLitre <= 0) throw new McpException("Price per litre must be greater than zero.");
        if (mileage <= 0) throw new McpException("An odometer reading must be greater than zero.");

        var entry = new FuelEntry
        {
            VehicleId = v.VehicleId,
            EntryDate = date,
            Mileage = mileage,
            Litres = litres,
            PricePerLitre = pricePerLitre,
            TotalCost = totalCost ?? decimal.Round(litres * pricePerLitre, 2),
            Station = station,
            FillLevel = fillLevel,
            Notes = notes,
        };

        await factory.CreateAsync(entry, Source, cancellationToken);
        var flags = await scanner.ScanAsync(v.VehicleId, Source, cancellationToken);

        // MPG is derived, so read it back from the summary rather than computing a second answer here.
        var summary = await metrics.GetVehicleSummaryAsync(v.VehicleId, cancellationToken);
        var mpg = summary?.Fuel.Entries.FirstOrDefault(e => e.FuelEntryId == entry.Id)?.Mpg;

        var mpgNote = mpg is { } m ? $" {m:0.0} mpg." : " MPG deferred to the next full fill.";
        var flagNote = flags.Count > 0 ? " Flagged (recorded anyway): " + string.Join("; ", flags.Select(f => f.Message)) + "." : "";
        return new McpResult<FuelFillResult>(
            $"Logged {litres:0.0} L at {mileage:N0} mi on {v.Registration}.{mpgNote}{flagNote}",
            new FuelFillResult(entry.Id, mpg, flags.ToFlags()));
    }

    [McpServerTool(Name = "add_service")]
    [Description(
        "Add a service or MOT record. Writes the record, its odometer reading and (when a cost is given) its "
        + "mirrored expense in one transaction. type is free text; use exactly \"MOT\" for an MOT so the expiry "
        + "derives from it. A mileage below the current odometer is flagged, never rejected.")]
    public static async Task<McpResult<AddedRow>> AddService(
        VehicleResolver resolver,
        ServiceRecordFactory factory,
        AnomalyScanner scanner,
        [Description("Date of the service (yyyy-MM-dd).")] DateOnly serviceDate,
        [Description("Service type. Use \"MOT\" exactly for an MOT; otherwise e.g. \"Service\", \"Repair\".")] string type,
        [Description("Odometer at the service.")] int mileage,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Garage name (created on first use).")] string? garage = null,
        [Description("What was done.")] string? workDone = null,
        [Description("Parts replaced.")] string? partsReplaced = null,
        [Description("Cost in £. When given, mirrors into expenses.")] decimal? cost = null,
        [Description("Next-due date (yyyy-MM-dd).")] DateOnly? nextDueDate = null,
        [Description("Next-due mileage.")] int? nextDueMileage = null,
        [Description("Free-text note.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        if (string.IsNullOrWhiteSpace(type))
            throw new McpException("A service record needs a type. \"MOT\" is matched exactly for the expiry.");

        var record = new ServiceRecord
        {
            VehicleId = v.VehicleId,
            ServiceDate = serviceDate,
            Type = type.Trim(),
            Mileage = mileage,
            Garage = garage,
            WorkDone = workDone,
            PartsReplaced = partsReplaced,
            Cost = cost,
            NextDueDate = nextDueDate,
            NextDueMileage = nextDueMileage,
            Notes = notes,
        };

        await factory.CreateAsync(record, Source, cancellationToken);
        var flags = await scanner.ScanAsync(v.VehicleId, Source, cancellationToken);

        var flagNote = flags.Count > 0 ? " Flagged (recorded anyway): " + string.Join("; ", flags.Select(f => f.Message)) + "." : "";
        return new McpResult<AddedRow>(
            $"Added {record.Type} at {mileage:N0} mi on {v.Registration}.{flagNote}",
            new AddedRow(record.Id, flags.ToFlags()));
    }

    [McpServerTool(Name = "add_vehicle")]
    [Description(
        "Add a vehicle to the garage, together with its opening odometer reading and the generic starter set of "
        + "regular checks. Registration must be unique. Example: registration \"BT53 AKJ\", make \"Land Rover\", "
        + "model \"Freelander\", year 2003, purchaseDate 2026-03-14, purchaseMileage 76632, fuelType Petrol.")]
    public static async Task<McpResult<AddedRow>> AddVehicle(
        VehicleResolver resolver,
        VehicleFactory factory,
        [Description("Registration plate.")] string registration,
        [Description("Make, e.g. \"Land Rover\".")] string make,
        [Description("Model, e.g. \"Freelander\".")] string model,
        [Description("Year of manufacture.")] int year,
        [Description("Purchase date (yyyy-MM-dd).")] DateOnly purchaseDate,
        [Description("Odometer at purchase.")] int purchaseMileage,
        [Description("Petrol, Diesel, Hybrid, Electric, …")] FuelType fuelType,
        [Description("Trim/variant, e.g. \"1.8 SE\".")] string? variant = null,
        [Description("Colour.")] string? colour = null,
        [Description("Engine code, e.g. \"18K4F\".")] string? engineCode = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(registration)) throw new McpException("A vehicle needs a registration.");

        var vehicle = new Vehicle
        {
            Registration = registration.Trim(),
            Make = make,
            Model = model,
            Year = year,
            PurchaseDate = purchaseDate,
            PurchaseMileage = purchaseMileage,
            FuelType = fuelType,
            Variant = variant,
            Colour = colour,
            EngineCode = engineCode,
            Source = Source,
        };

        // Token by name: the starter-check-selection params sit before it (CLAUDE.md).
        await factory.CreateAsync(vehicle, Source, cancellationToken: cancellationToken);
        return new McpResult<AddedRow>($"Added {vehicle.Registration} ({make} {model}) to the garage.", new AddedRow(vehicle.Id, []));
    }

    [McpServerTool(Name = "add_task")]
    [Description(
        "Add a DIY or Workshop task. kind DIY (do it yourself) or Workshop (pay a garage); priority Low/Medium/High. "
        + "Example: title \"Replace front pads\", kind Workshop, priority High, estimatedCost 180.")]
    public static async Task<McpResult<TaskItem>> AddTask(
        VehicleResolver resolver,
        TaskService tasks,
        [Description("What needs doing.")] string title,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("DIY or Workshop.")] MaintenanceTaskKind kind = MaintenanceTaskKind.DIY,
        [Description("Low, Medium or High.")] Priority priority = Priority.Medium,
        [Description("Longer description.")] string? description = null,
        [Description("Estimated cost in £.")] decimal? estimatedCost = null,
        [Description("Target date (yyyy-MM-dd).")] DateOnly? targetDate = null,
        [Description("Garage to do it (created on first use).")] string? assignedGarage = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var input = new TaskInput(title, kind, priority, MaintenanceTaskStatus.Open, description, estimatedCost, targetDate, null, assignedGarage);
        var result = await tasks.AddAsync(v.VehicleId, input, Source, cancellationToken);
        return ToolHelpers.ToResult(result, $"Added task \"{title}\" ({kind}) on {v.Registration}.");
    }

    [McpServerTool(Name = "complete_task")]
    [Description(
        "Mark a task done, stamping its completed date (defaults to today). To turn a completed Workshop task into "
        + "a service-history record, use the web app's promote action after completing.")]
    public static async Task<McpResult<TaskItem>> CompleteTask(
        VehicleResolver resolver,
        TaskService tasks,
        [Description("The task's id (from get_open_tasks).")] int taskId,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Completed date (yyyy-MM-dd). Omit for today.")] DateOnly? completedDate = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var result = await tasks.CompleteAsync(v.VehicleId, taskId, completedDate, Source, cancellationToken);
        return ToolHelpers.ToResult(result, $"Marked task {taskId} done on {v.Registration}.");
    }

    // ---- service-backed --------------------------------------------------------------------------------

    [McpServerTool(Name = "log_expense")]
    [Description(
        "Record an expense. category must be an existing category (Repair, Tax, Wash, Misc, Tools/Equipment, …) — "
        + "Fuel is refused here, as fuel expenses come from the fuel log. A mileage, if given, also writes an "
        + "odometer reading. Example: category \"Repair\", amount 120.50, vendor \"Kwik Fit\".")]
    public static async Task<McpResult<ExpenseItem>> LogExpense(
        VehicleResolver resolver,
        ExpenseService expenses,
        [Description("Expense category (not Fuel).")] string category,
        [Description("Amount in £.")] decimal amount,
        [Description("Date (yyyy-MM-dd).")] DateOnly date,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Sub-category.")] string? subCategory = null,
        [Description("Vendor / who was paid.")] string? vendor = null,
        [Description("Odometer at the expense, if known.")] int? mileage = null,
        [Description("Payment method.")] string? paymentMethod = null,
        [Description("Free-text note.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var input = new ExpenseInput(date, category, amount, subCategory, vendor, mileage, paymentMethod, notes);
        var result = await expenses.AddAsync(v.VehicleId, input, Source, cancellationToken);
        return ToolHelpers.ToResult(result, $"Logged £{amount:N2} {category} on {v.Registration}.");
    }

    [McpServerTool(Name = "update_mileage")]
    [Description(
        "Record a quick manual odometer reading. A reading below the current odometer is flagged, never rejected. "
        + "Example: date 2026-07-20, mileage 80920.")]
    public static async Task<McpResult<MileageReadingItem>> UpdateMileage(
        VehicleResolver resolver,
        LogWriteService writes,
        [Description("Date (yyyy-MM-dd).")] DateOnly date,
        [Description("Odometer reading.")] int mileage,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Free-text note.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var result = await writes.AddMileageAsync(v.VehicleId, new MileageInput(date, mileage, notes), Source, cancellationToken);
        return ToolHelpers.ToResult(result, $"Recorded {mileage:N0} mi on {v.Registration}.");
    }

    [McpServerTool(Name = "mark_check_done")]
    [Description(
        "Mark a regular check as done today (or on performedOn). checkName is matched to an active check by name "
        + "(see get_check_status). result is optional: OK, Attention or Failed — use Attention for e.g. \"mayo "
        + "under the oil filler cap\", which the head-gasket watch depends on noticing.")]
    public static async Task<McpResult<Shared.Metrics.CheckStatusSummary>> MarkCheckDone(
        VehicleResolver resolver,
        CheckService checks,
        [Description("The check's name, e.g. \"Engine oil level\".")] string checkName,
        [Description("When it was done (yyyy-MM-dd).")] DateOnly performedOn,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("OK, Attention or Failed. Omit for a plain 'done, all fine'.")] CheckResult? result = null,
        [Description("Free-text note.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var write = await checks.MarkDoneByNameAsync(v.VehicleId, checkName, performedOn, result, notes, Source, cancellationToken);
        return ToolHelpers.ToResult(write, $"Marked \"{checkName}\" done on {v.Registration}.");
    }

    [McpServerTool(Name = "log_wash")]
    [Description("Record a wash. location is created on first use. Example: date 2026-07-20, location \"Home\", washType \"Underbody rinse\".")]
    public static async Task<McpResult<WashItem>> LogWash(
        VehicleResolver resolver,
        LogWriteService writes,
        [Description("Date (yyyy-MM-dd).")] DateOnly date,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Where it was washed (created on first use).")] string? location = null,
        [Description("Wash type, e.g. \"Underbody rinse\".")] string? washType = null,
        [Description("Cost in £.")] decimal? cost = null,
        [Description("Odometer, if known.")] int? mileage = null,
        [Description("Free-text note.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var result = await writes.AddWashAsync(v.VehicleId, new WashInput(date, location, washType, cost, mileage, notes), Source, cancellationToken);
        return ToolHelpers.ToResult(result, $"Logged a wash on {v.Registration}.");
    }

    [McpServerTool(Name = "log_tyre_reading")]
    [Description(
        "Record a tyre reading — pressures (PSI) and tread depths (mm) by corner, plus spare pressure. All values "
        + "optional; a supplied mileage also writes an odometer reading. Corners: fl=front-left, fr=front-right, "
        + "rl=rear-left, rr=rear-right.")]
    public static async Task<McpResult<TyreReadingItem>> LogTyreReading(
        VehicleResolver resolver,
        LogWriteService writes,
        [Description("Date (yyyy-MM-dd).")] DateOnly date,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Odometer, if known.")] int? mileage = null,
        [Description("Front-left pressure (PSI).")] decimal? psiFrontLeft = null,
        [Description("Front-right pressure (PSI).")] decimal? psiFrontRight = null,
        [Description("Rear-left pressure (PSI).")] decimal? psiRearLeft = null,
        [Description("Rear-right pressure (PSI).")] decimal? psiRearRight = null,
        [Description("Spare pressure (PSI).")] decimal? psiSpare = null,
        [Description("Front-left tread (mm).")] decimal? treadFrontLeft = null,
        [Description("Front-right tread (mm).")] decimal? treadFrontRight = null,
        [Description("Rear-left tread (mm).")] decimal? treadRearLeft = null,
        [Description("Rear-right tread (mm).")] decimal? treadRearRight = null,
        [Description("Where taken.")] string? location = null,
        [Description("Gauge/tool used.")] string? tool = null,
        [Description("Free-text note.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var input = new TyreInput(date, mileage, psiFrontLeft, psiFrontRight, psiRearLeft, psiRearRight, psiSpare,
            treadFrontLeft, treadFrontRight, treadRearLeft, treadRearRight, location, tool, notes);
        var result = await writes.AddTyreAsync(v.VehicleId, input, Source, cancellationToken);
        return ToolHelpers.ToResult(result, $"Logged a tyre reading on {v.Registration}.");
    }

    [McpServerTool(Name = "add_equipment")]
    [Description("Add an equipment/kit item to the inventory. status Owned, OnOrder or ToBuy. Example: name \"Recovery straps\", status Owned.")]
    public static async Task<McpResult<EquipmentItemDto>> AddEquipment(
        VehicleResolver resolver,
        LogWriteService writes,
        [Description("Item name.")] string name,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Owned, OnOrder or ToBuy.")] EquipmentStatus status = EquipmentStatus.Owned,
        [Description("Category.")] string? category = null,
        [Description("Purchase date (yyyy-MM-dd).")] DateOnly? purchasedDate = null,
        [Description("Where bought.")] string? sourceVendor = null,
        [Description("Cost in £.")] decimal? cost = null,
        [Description("Where stored.")] string? storedAt = null,
        [Description("Free-text note.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var input = new EquipmentInput(name, status, category, purchasedDate, sourceVendor, cost, storedAt, notes);
        var result = await writes.AddEquipmentAsync(v.VehicleId, input, Source, cancellationToken);
        return ToolHelpers.ToResult(result, $"Added \"{name}\" to {v.Registration}'s kit.");
    }

    [McpServerTool(Name = "add_issue")]
    [Description(
        "Add an issue to the watchlist — something wrong that is being monitored, not yet a job. severity "
        + "Low/Medium/High. Example: title \"Brake pipe corrosion\", firstNoted 2026-04-01, severity Medium, "
        + "currentObservation \"surface rust, advisory\".")]
    public static async Task<McpResult<IssueItem>> AddIssue(
        VehicleResolver resolver,
        IssueService issues,
        [Description("Short title.")] string title,
        [Description("When first noted (yyyy-MM-dd).")] DateOnly firstNoted,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Low, Medium or High.")] Severity severity = Severity.Low,
        [Description("Current observation.")] string? currentObservation = null,
        [Description("What to do if it worsens.")] string? actionIfWorsens = null,
        [Description("Estimated fix cost in £.")] decimal? estimatedFixCost = null,
        [Description("Free-text note.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var input = new IssueInput(title, firstNoted, severity, IssueStatus.Monitoring, null, currentObservation, actionIfWorsens, estimatedFixCost, notes);
        var result = await issues.AddAsync(v.VehicleId, input, Source, cancellationToken);
        return ToolHelpers.ToResult(result, $"Added issue \"{title}\" to {v.Registration}'s watchlist.");
    }

    [McpServerTool(Name = "add_issue_observation")]
    [Description(
        "Record a fresh observation on a watchlist issue — updates its last-checked date and current observation, "
        + "which is how the watchlist notices something has been worsening. Use get_issues for the issue id.")]
    public static async Task<McpResult<IssueItem>> AddIssueObservation(
        VehicleResolver resolver,
        IssueService issues,
        [Description("The issue's id (from get_issues).")] int issueId,
        [Description("When checked (yyyy-MM-dd).")] DateOnly lastChecked,
        [Description("What it looks like now.")] string currentObservation,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var result = await issues.AddObservationAsync(v.VehicleId, issueId, lastChecked, currentObservation, Source, cancellationToken);
        return ToolHelpers.ToResult(result, $"Recorded an observation on issue {issueId} for {v.Registration}.");
    }

    // ---- vehicle settings (drive the renewal countdowns) -----------------------------------------------

    [McpServerTool(Name = "set_insurance")]
    [Description(
        "Record a vehicle's insurance policy — this is what makes the insurance renewal show up and warn ahead of "
        + "time. periodEnd is the renewal date. Example: insurer \"Admiral\", coverType \"Comprehensive\", "
        + "periodStart 2026-02-01, periodEnd 2027-01-31. Omitted fields are left unchanged.")]
    public static async Task<McpResult<RenewalSummary>> SetInsurance(
        VehicleResolver resolver,
        VehicleUpdateService updates,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Insurer, e.g. \"Admiral\".")] string? insurer = null,
        [Description("Policy number.")] string? policyNumber = null,
        [Description("Cover start date (yyyy-MM-dd).")] DateOnly? periodStart = null,
        [Description("Cover end / renewal date (yyyy-MM-dd) — drives the renewal countdown.")] DateOnly? periodEnd = null,
        [Description("Cover type, e.g. \"Comprehensive\", \"Third party\".")] string? coverType = null,
        [Description("Annual premium in £.")] decimal? premium = null,
        [Description("Compulsory excess in £.")] decimal? excessCompulsory = null,
        [Description("Voluntary excess in £.")] decimal? excessVoluntary = null,
        [Description("No-claims-bonus years.")] int? ncbYears = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var patch = new VehiclePatch(Insurance: new InsurancePatch(
            insurer, policyNumber, periodStart, periodEnd, coverType, premium, excessCompulsory, excessVoluntary, ncbYears));

        var s = await ApplyOrThrowAsync(updates, v, patch, cancellationToken);
        return new McpResult<RenewalSummary>($"{v.Registration}: {Describe("Insurance", s.Renewals.Insurance)}", s.Renewals);
    }

    [McpServerTool(Name = "set_road_tax")]
    [Description(
        "Record a vehicle's road tax (VED) — this is what makes the road-tax renewal show up. vedExpiry is the "
        + "renewal date. VED runs on its own 12-month cycle, independent of insurance. Example: vedExpiry "
        + "2027-01-31, vedAnnualCost 180. Omitted fields are left unchanged.")]
    public static async Task<McpResult<RenewalSummary>> SetRoadTax(
        VehicleResolver resolver,
        VehicleUpdateService updates,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("VED expiry / renewal date (yyyy-MM-dd) — drives the renewal countdown.")] DateOnly? vedExpiry = null,
        [Description("Annual road-tax cost in £.")] decimal? vedAnnualCost = null,
        [Description("Whether the vehicle is ULEZ compliant.")] bool? ulezCompliant = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var patch = new VehiclePatch(VedExpiry: vedExpiry, VedAnnualCost: vedAnnualCost, UlezCompliant: ulezCompliant);

        var s = await ApplyOrThrowAsync(updates, v, patch, cancellationToken);
        return new McpResult<RenewalSummary>($"{v.Registration}: {Describe("Road tax", s.Renewals.RoadTax)}", s.Renewals);
    }

    [McpServerTool(Name = "update_vehicle_profile")]
    [Description(
        "Update a vehicle's basic stored details — colour, VIN, body style, where it was bought, its default "
        + "garage, notes, and usable fuel-tank capacity (which drives the full-tank range). Omitted fields are "
        + "left unchanged. This does not change MOT/insurance/tax dates (use set_insurance / set_road_tax / "
        + "add_service) or which car is the default.")]
    public static async Task<McpResult<VehicleIdentity>> UpdateVehicleProfile(
        VehicleResolver resolver,
        VehicleUpdateService updates,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Colour.")] string? colour = null,
        [Description("VIN.")] string? vin = null,
        [Description("Body style, e.g. \"5-door SUV\".")] string? bodyStyle = null,
        [Description("Who it was bought from.")] string? seller = null,
        [Description("Default garage name (created on first use).")] string? defaultGarage = null,
        [Description("Free-text notes about the vehicle.")] string? notes = null,
        [Description("Usable fuel-tank capacity in litres — drives the full-tank range estimate.")] decimal? fuelTankCapacityLitres = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var patch = new VehiclePatch(
            Colour: colour, Vin: vin, BodyStyle: bodyStyle, Seller: seller, DefaultGarage: defaultGarage, Notes: notes,
            // Only send a Fluids block when a capacity is given, so omitting it leaves the value untouched rather
            // than clearing it (the block is an authoritative set).
            Fluids: fuelTankCapacityLitres is { } cap ? new FluidsPatch(cap) : null);

        var s = await ApplyOrThrowAsync(updates, v, patch, cancellationToken);
        return new McpResult<VehicleIdentity>($"Updated {v.Registration}'s details.", s.Identity);
    }

    private static async Task<VehicleSummary> ApplyOrThrowAsync(
        VehicleUpdateService updates, VehicleRef v, VehiclePatch patch, CancellationToken cancellationToken)
    {
        var result = await updates.ApplyAsync(v.VehicleId, patch, cancellationToken);
        if (result.Status == WriteStatus.Validation)
            throw new McpException("Rejected — " + string.Join(" ", result.Errors!.SelectMany(e => e.Value)));
        return result.Value ?? throw new McpException($"Could not update {v.Registration}.");
    }

    private static string Describe(string label, Renewal renewal) =>
        renewal.ExpiryDate is { } expiry
            ? $"{label} now renews {expiry:d MMM yyyy}" + (renewal.DaysRemaining is { } d ? $" ({d} days)." : ".")
            : $"{label} has no renewal date set.";
}

/// <param name="Mpg">The fill's computed MPG, or null when a partial fill defers it to the next full one.</param>
public sealed record FuelFillResult(int Id, decimal? Mpg, IReadOnlyList<AnomalyFlag> Flags);

/// <summary>A created row's id and any integrity flags the write raised.</summary>
public sealed record AddedRow(int Id, IReadOnlyList<AnomalyFlag> Flags);
