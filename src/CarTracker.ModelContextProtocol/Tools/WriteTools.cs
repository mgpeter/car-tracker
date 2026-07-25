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
using Microsoft.EntityFrameworkCore;
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
        CarTrackerDbContext context,
        ICurrentUserAccessor currentUser,
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

        // The token acts as its owner; the new vehicle belongs to that user. A legacy token with no owner
        // cannot create one (it would be an orphan visible to nobody).
        if (currentUser.OwnerId is not int ownerId)
            throw new McpException("This token is not linked to a user account, so it cannot create a vehicle.");

        // A friendly, named conflict instead of the SDK's opaque "An error occurred" the unique index would
        // otherwise produce. Scoped to this owner by the vehicle query filter, so it catches a plate this user
        // already has while letting a different user register the same one — the same rule the web create uses.
        var normalized = VehicleResolver.Normalize(registration);
        if (await context.Vehicles.AsNoTracking()
                .AnyAsync(v => EF.Property<string>(v, "RegistrationNormalized") == normalized, cancellationToken))
        {
            throw new McpException($"Registration already exists: a vehicle registered '{registration.Trim()}' is already in your garage.");
        }

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

        try
        {
            // Token by name: the starter-check-selection params sit before it (CLAUDE.md).
            await factory.CreateAsync(vehicle, ownerId, Source, cancellationToken: cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The race the pre-check cannot close: two creates pass the check together and the unique index
            // rejects the loser. Surface it as the same clean conflict rather than an opaque failure.
            throw new McpException($"Registration already exists: a vehicle registered '{registration.Trim()}' is already in your garage.");
        }

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
        "Mark a regular check as done on performedOn. Identify it by checkDefinitionId (from get_check_status — "
        + "robust to the em dashes and ampersands in seeded names) or by checkName (matched to an active check). "
        + "result is optional: OK, Attention or Failed — use Attention for e.g. \"mayo under the oil filler cap\", "
        + "which the head-gasket watch depends on noticing. Returns just the affected check and the new status counts.")]
    public static async Task<McpResult<CheckMarkResult>> MarkCheckDone(
        VehicleResolver resolver,
        CheckService checks,
        [Description("When it was done (yyyy-MM-dd).")] DateOnly performedOn,
        [Description("The check's definition id (from get_check_status). Preferred — exact. Omit if using checkName.")] int? checkDefinitionId = null,
        [Description("The check's name, e.g. \"Engine oil level\". Used if checkDefinitionId is omitted.")] string? checkName = null,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("OK, Attention or Failed. Omit for a plain 'done, all fine'.")] CheckResult? result = null,
        [Description("Free-text note.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var write = await checks.MarkSingleDoneAsync(v.VehicleId, checkDefinitionId, checkName, performedOn, result, notes, Source, cancellationToken);
        var label = checkName is { Length: > 0 } ? $"\"{checkName}\"" : $"check {checkDefinitionId}";
        return ToolHelpers.ToResult(write, $"Marked {label} done on {v.Registration}.");
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
    [Description("Add an equipment/kit item to the inventory. status Owned, OnOrder or ToOrder. Example: name \"Recovery straps\", status Owned.")]
    public static async Task<McpResult<EquipmentItemDto>> AddEquipment(
        VehicleResolver resolver,
        LogWriteService writes,
        [Description("Item name.")] string name,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Owned, OnOrder or ToOrder.")] EquipmentStatus status = EquipmentStatus.Owned,
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
        [Description("Cover type, e.g. \"Comprehensive\", \"Third party\" (40 characters max).")] string? coverType = null,
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

    [McpServerTool(Name = "set_fluids")]
    [Description(
        "Set a vehicle's fluid and consumable reference specs — the \"what goes in it\" facts get_reference reads: "
        + "oil spec/capacity, coolant spec/capacity, brake and transmission fluid, and part numbers (spark plug, "
        + "oil/air/fuel/cabin filters). coolantSpec is important for BT53's K-series head gasket — it must be OAT "
        + "(red/pink), never mixed with IAT. Omitted fields are left unchanged. Example: oilSpec \"5W-30 ACEA A3/B4\", "
        + "coolantSpec \"OAT (red)\", oilFilterPart \"W712/75\".")]
    public static async Task<McpResult<VehicleReference>> SetFluids(
        VehicleResolver resolver,
        VehicleUpdateService updates,
        LogQueryService queries,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Engine oil specification, e.g. \"5W-30 ACEA A3/B4\".")] string? oilSpec = null,
        [Description("Oil capacity in litres.")] decimal? oilCapacityLitres = null,
        [Description("Coolant specification — must be OAT (red/pink) for the K-series, never IAT.")] string? coolantSpec = null,
        [Description("Coolant capacity in litres.")] decimal? coolantCapacityLitres = null,
        [Description("Brake fluid specification, e.g. \"DOT 4\".")] string? brakeFluidSpec = null,
        [Description("Transmission/gearbox oil specification.")] string? transmissionOilSpec = null,
        [Description("Spark plug part number.")] string? sparkPlugPart = null,
        [Description("Oil filter part number.")] string? oilFilterPart = null,
        [Description("Air filter part number.")] string? airFilterPart = null,
        [Description("Fuel filter part number.")] string? fuelFilterPart = null,
        [Description("Cabin/pollen filter part number.")] string? cabinFilterPart = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var patch = new VehiclePatch(Fluids: new FluidsPatch(
            OilSpec: oilSpec, OilCapacityLitres: oilCapacityLitres, CoolantSpec: coolantSpec,
            CoolantCapacityLitres: coolantCapacityLitres, BrakeFluidSpec: brakeFluidSpec,
            TransmissionOilSpec: transmissionOilSpec, SparkPlugPart: sparkPlugPart, OilFilterPart: oilFilterPart,
            AirFilterPart: airFilterPart, FuelFilterPart: fuelFilterPart, CabinFilterPart: cabinFilterPart));

        await ApplyOrThrowAsync(updates, v, patch, cancellationToken);
        var reference = await queries.GetReferenceAsync(v.VehicleId, cancellationToken)
            ?? throw new McpException($"Could not load {v.Registration}.");
        return new McpResult<VehicleReference>($"Updated {v.Registration}'s fluid and consumable specs.", reference);
    }

    [McpServerTool(Name = "set_tyre_specs")]
    [Description(
        "Set a vehicle's tyre reference specs — size, the manufacturer's cold pressures (normal and laden/full "
        + "load) and the minimum legal tread. These are the reference figures get_reference answers \"what pressure "
        + "for a full load\" with — not a reading (log a measured reading with log_tyre_reading). Omitted fields are "
        + "left unchanged. Example: tyreSize \"215/65 R16\", pressureFrontPsi 30, pressureRearPsi 33, "
        + "pressureRearLadenPsi 38, minTreadMm 1.6.")]
    public static async Task<McpResult<VehicleReference>> SetTyreSpecs(
        VehicleResolver resolver,
        VehicleUpdateService updates,
        LogQueryService queries,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Tyre size, e.g. \"215/65 R16\".")] string? tyreSize = null,
        [Description("Front cold pressure (PSI), normal load.")] decimal? pressureFrontPsi = null,
        [Description("Rear cold pressure (PSI), normal load.")] decimal? pressureRearPsi = null,
        [Description("Front cold pressure (PSI), fully laden.")] decimal? pressureFrontLadenPsi = null,
        [Description("Rear cold pressure (PSI), fully laden.")] decimal? pressureRearLadenPsi = null,
        [Description("Minimum legal tread depth (mm) — 1.6 in the UK.")] decimal? minTreadMm = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var patch = new VehiclePatch(Tyres: new TyresPatch(
            TyreSize: tyreSize, PressureFrontPsi: pressureFrontPsi, PressureRearPsi: pressureRearPsi,
            PressureFrontLadenPsi: pressureFrontLadenPsi, PressureRearLadenPsi: pressureRearLadenPsi,
            MinTreadMm: minTreadMm));

        await ApplyOrThrowAsync(updates, v, patch, cancellationToken);
        var reference = await queries.GetReferenceAsync(v.VehicleId, cancellationToken)
            ?? throw new McpException($"Could not load {v.Registration}.");
        return new McpResult<VehicleReference>($"Updated {v.Registration}'s tyre specs.", reference);
    }

    // ---- edit / delete -----------------------------------------------------------------------------------

    [McpServerTool(Name = "update_fuel_fillup")]
    [Description(
        "Edit an existing fuel fill-up (id from list_fuel_fillups). Its odometer reading and mirrored expense "
        + "follow and MPG re-derives, all in one transaction. Omitted fields are left unchanged; the receipt total "
        + "recomputes from litres × price only when one of those changes. A mileage below the odometer is flagged, "
        + "never rejected.")]
    public static async Task<McpResult<FuelFillResult>> UpdateFuelFillup(
        VehicleResolver resolver,
        CarTrackerDbContext context,
        FuelEntryFactory factory,
        AnomalyScanner scanner,
        IDerivedMetricsService metrics,
        [Description("The fill's id (from list_fuel_fillups).")] int id,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Date of the fill (yyyy-MM-dd).")] DateOnly? date = null,
        [Description("Odometer at the fill.")] int? mileage = null,
        [Description("Litres pumped.")] decimal? litres = null,
        [Description("Price per litre in £.")] decimal? pricePerLitre = null,
        [Description("Receipt total in £.")] decimal? totalCost = null,
        [Description("Filling station.")] string? station = null,
        [Description("Full, Half or Quarter.")] FillLevel? fillLevel = null,
        [Description("Free-text note.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var entry = await context.FuelEntries
            .FirstOrDefaultAsync(f => f.Id == id && f.VehicleId == v.VehicleId, cancellationToken);
        if (entry is null)
            throw new McpException($"No fuel fill-up {id} on {v.Registration}. Use list_fuel_fillups to find the id.");

        if (litres is <= 0) throw new McpException("A fill must have litres — they are the sole basis of MPG.");
        if (pricePerLitre is <= 0) throw new McpException("Price per litre must be greater than zero.");
        if (mileage is <= 0) throw new McpException("An odometer reading must be greater than zero.");
        if (totalCost is <= 0) throw new McpException("A total must be greater than zero, or omitted to compute it.");

        // The reading carries no FK back, so its old key is captured before the edit moves it.
        var originalDate = entry.EntryDate;
        var originalMileage = entry.Mileage;

        entry.EntryDate = date ?? entry.EntryDate;
        entry.Mileage = mileage ?? entry.Mileage;
        entry.Litres = litres ?? entry.Litres;
        entry.PricePerLitre = pricePerLitre ?? entry.PricePerLitre;
        entry.TotalCost = totalCost
            ?? (litres is not null || pricePerLitre is not null
                ? decimal.Round(entry.Litres * entry.PricePerLitre, 2)
                : entry.TotalCost);
        entry.Station = station ?? entry.Station;
        entry.FillLevel = fillLevel ?? entry.FillLevel;
        entry.Notes = notes ?? entry.Notes;

        await factory.UpdateAsync(entry, originalDate, originalMileage, cancellationToken);
        var flags = await scanner.ScanAsync(v.VehicleId, Source, cancellationToken);

        var summary = await metrics.GetVehicleSummaryAsync(v.VehicleId, cancellationToken);
        var mpg = summary?.Fuel.Entries.FirstOrDefault(e => e.FuelEntryId == entry.Id)?.Mpg;
        var mpgNote = mpg is { } m ? $" {m:0.0} mpg." : "";
        var flagNote = flags.Count > 0 ? " Flagged: " + string.Join("; ", flags.Select(f => f.Message)) + "." : "";
        return new McpResult<FuelFillResult>(
            $"Updated fill {id} on {v.Registration}.{mpgNote}{flagNote}",
            new FuelFillResult(entry.Id, mpg, flags.ToFlags()));
    }

    [McpServerTool(Name = "delete_fuel_fillup")]
    [Description(
        "Delete a fuel fill-up (id from list_fuel_fillups). Its odometer reading and mirrored expense go with it, "
        + "then the detectors re-run (removing a fill can clear a flag it caused).")]
    public static async Task<McpResult<DeletedRow>> DeleteFuelFillup(
        VehicleResolver resolver,
        CarTrackerDbContext context,
        FuelEntryFactory factory,
        AnomalyScanner scanner,
        [Description("The fill's id (from list_fuel_fillups).")] int id,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var entry = await context.FuelEntries
            .FirstOrDefaultAsync(f => f.Id == id && f.VehicleId == v.VehicleId, cancellationToken);
        if (entry is null)
            throw new McpException($"No fuel fill-up {id} on {v.Registration}. Use list_fuel_fillups to find the id.");

        await factory.DeleteAsync(entry, cancellationToken);
        await scanner.ScanAsync(v.VehicleId, Source, cancellationToken);
        return new McpResult<DeletedRow>($"Deleted fill {id} from {v.Registration}.", new DeletedRow(id));
    }

    [McpServerTool(Name = "update_service")]
    [Description(
        "Edit an existing service/MOT record (id from list_service_history). Its odometer reading and mirrored "
        + "expense follow. Omitted fields are left unchanged. Use \"MOT\" exactly for the expiry to derive from it.")]
    public static async Task<McpResult<AddedRow>> UpdateService(
        VehicleResolver resolver,
        CarTrackerDbContext context,
        ServiceRecordFactory factory,
        AnomalyScanner scanner,
        [Description("The record's id (from list_service_history).")] int id,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Date of the service (yyyy-MM-dd).")] DateOnly? serviceDate = null,
        [Description("Service type. \"MOT\" exactly for an MOT.")] string? type = null,
        [Description("Odometer at the service.")] int? mileage = null,
        [Description("Garage name (created on first use).")] string? garage = null,
        [Description("What was done.")] string? workDone = null,
        [Description("Parts replaced.")] string? partsReplaced = null,
        [Description("Cost in £.")] decimal? cost = null,
        [Description("Next-due date (yyyy-MM-dd).")] DateOnly? nextDueDate = null,
        [Description("Next-due mileage.")] int? nextDueMileage = null,
        [Description("Free-text note.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var record = await context.ServiceRecords
            .FirstOrDefaultAsync(r => r.Id == id && r.VehicleId == v.VehicleId, cancellationToken);
        if (record is null)
            throw new McpException($"No service record {id} on {v.Registration}. Use list_service_history to find the id.");

        var originalDate = record.ServiceDate;
        var originalMileage = record.Mileage;

        record.ServiceDate = serviceDate ?? record.ServiceDate;
        record.Type = type ?? record.Type;
        record.Mileage = mileage ?? record.Mileage;
        record.Garage = garage ?? record.Garage;
        record.WorkDone = workDone ?? record.WorkDone;
        record.PartsReplaced = partsReplaced ?? record.PartsReplaced;
        record.Cost = cost ?? record.Cost;
        record.NextDueDate = nextDueDate ?? record.NextDueDate;
        record.NextDueMileage = nextDueMileage ?? record.NextDueMileage;
        record.Notes = notes ?? record.Notes;

        await factory.UpdateAsync(record, originalDate, originalMileage, cancellationToken);
        var flags = await scanner.ScanAsync(v.VehicleId, Source, cancellationToken);

        var flagNote = flags.Count > 0 ? " Flagged: " + string.Join("; ", flags.Select(f => f.Message)) + "." : "";
        return new McpResult<AddedRow>($"Updated service record {id} on {v.Registration}.{flagNote}", new AddedRow(record.Id, flags.ToFlags()));
    }

    [McpServerTool(Name = "delete_service")]
    [Description("Delete a service/MOT record (id from list_service_history). Its mirrored reading and expense go with it.")]
    public static async Task<McpResult<DeletedRow>> DeleteService(
        VehicleResolver resolver,
        CarTrackerDbContext context,
        ServiceRecordFactory factory,
        AnomalyScanner scanner,
        [Description("The record's id (from list_service_history).")] int id,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var record = await context.ServiceRecords
            .FirstOrDefaultAsync(r => r.Id == id && r.VehicleId == v.VehicleId, cancellationToken);
        if (record is null)
            throw new McpException($"No service record {id} on {v.Registration}. Use list_service_history to find the id.");

        await factory.DeleteAsync(record, cancellationToken);
        await scanner.ScanAsync(v.VehicleId, Source, cancellationToken);
        return new McpResult<DeletedRow>($"Deleted service record {id} from {v.Registration}.", new DeletedRow(id));
    }

    [McpServerTool(Name = "update_mileage_reading")]
    [Description(
        "Edit a manual odometer reading (id from list_mileage). Only a Manual reading is editable — a fuel/service/"
        + "tyre/wash reading is a shadow, corrected through its source. A reading below the odometer is flagged, "
        + "never rejected.")]
    public static async Task<McpResult<MileageReadingItem>> UpdateMileageReading(
        VehicleResolver resolver,
        LogWriteService writes,
        [Description("The reading's id (from list_mileage).")] int id,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Date (yyyy-MM-dd).")] DateOnly? date = null,
        [Description("Odometer reading.")] int? mileage = null,
        [Description("Free-text note.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var result = await writes.UpdateMileageAsync(v.VehicleId, id, new MileagePatch(date, mileage, notes), Source, cancellationToken);
        return ToolHelpers.ToResult(result, $"Updated reading {id} on {v.Registration}.");
    }

    [McpServerTool(Name = "delete_mileage_reading")]
    [Description("Delete a manual odometer reading (id from list_mileage). A shadow reading refuses — edit its source instead.")]
    public static async Task<McpResult<DeletedRow>> DeleteMileageReading(
        VehicleResolver resolver,
        LogWriteService writes,
        [Description("The reading's id (from list_mileage).")] int id,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var result = await writes.DeleteMileageAsync(v.VehicleId, id, Source, cancellationToken);
        return ToDeleteResult(result, id, $"Deleted reading {id} from {v.Registration}.");
    }

    [McpServerTool(Name = "update_tyre_reading")]
    [Description("Edit a tyre reading (id from list_tyre_readings). Its odometer shadow follows. Omitted fields are left unchanged.")]
    public static async Task<McpResult<TyreReadingItem>> UpdateTyreReading(
        VehicleResolver resolver,
        LogWriteService writes,
        [Description("The reading's id (from list_tyre_readings).")] int id,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Date (yyyy-MM-dd).")] DateOnly? date = null,
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
        var patch = new TyrePatch(date, mileage, psiFrontLeft, psiFrontRight, psiRearLeft, psiRearRight, psiSpare,
            treadFrontLeft, treadFrontRight, treadRearLeft, treadRearRight, location, tool, notes);
        var result = await writes.UpdateTyreAsync(v.VehicleId, id, patch, Source, cancellationToken);
        return ToolHelpers.ToResult(result, $"Updated tyre reading {id} on {v.Registration}.");
    }

    [McpServerTool(Name = "delete_tyre_reading")]
    [Description("Delete a tyre reading (id from list_tyre_readings). Its odometer shadow goes with it.")]
    public static async Task<McpResult<DeletedRow>> DeleteTyreReading(
        VehicleResolver resolver,
        LogWriteService writes,
        [Description("The reading's id (from list_tyre_readings).")] int id,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var result = await writes.DeleteTyreAsync(v.VehicleId, id, Source, cancellationToken);
        return ToDeleteResult(result, id, $"Deleted tyre reading {id} from {v.Registration}.");
    }

    [McpServerTool(Name = "update_wash")]
    [Description("Edit a wash (id from list_wash_log). A new location name is created on first use. Omitted fields are left unchanged.")]
    public static async Task<McpResult<WashItem>> UpdateWash(
        VehicleResolver resolver,
        LogWriteService writes,
        [Description("The wash's id (from list_wash_log).")] int id,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Date (yyyy-MM-dd).")] DateOnly? date = null,
        [Description("Where it was washed (created on first use).")] string? location = null,
        [Description("Wash type.")] string? washType = null,
        [Description("Cost in £.")] decimal? cost = null,
        [Description("Odometer, if known.")] int? mileage = null,
        [Description("Free-text note.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var result = await writes.UpdateWashAsync(v.VehicleId, id, new WashPatch(date, location, washType, cost, mileage, notes), Source, cancellationToken);
        return ToolHelpers.ToResult(result, $"Updated wash {id} on {v.Registration}.");
    }

    [McpServerTool(Name = "delete_wash")]
    [Description("Delete a wash entry (id from list_wash_log).")]
    public static async Task<McpResult<DeletedRow>> DeleteWash(
        VehicleResolver resolver,
        LogWriteService writes,
        [Description("The wash's id (from list_wash_log).")] int id,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var result = await writes.DeleteWashAsync(v.VehicleId, id, Source, cancellationToken);
        return ToDeleteResult(result, id, $"Deleted wash {id} from {v.Registration}.");
    }

    [McpServerTool(Name = "update_equipment")]
    [Description("Edit an equipment/kit item (id from list_equipment). status Owned, OnOrder or ToOrder. Omitted fields are left unchanged.")]
    public static async Task<McpResult<EquipmentItemDto>> UpdateEquipment(
        VehicleResolver resolver,
        LogWriteService writes,
        [Description("The item's id (from list_equipment).")] int id,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Item name.")] string? name = null,
        [Description("Owned, OnOrder or ToOrder.")] EquipmentStatus? status = null,
        [Description("Category.")] string? category = null,
        [Description("Purchase date (yyyy-MM-dd).")] DateOnly? purchasedDate = null,
        [Description("Where bought.")] string? sourceVendor = null,
        [Description("Cost in £.")] decimal? cost = null,
        [Description("Where stored.")] string? storedAt = null,
        [Description("Free-text note.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var patch = new EquipmentPatch(name, status, category, purchasedDate, sourceVendor, cost, storedAt, notes);
        var result = await writes.UpdateEquipmentAsync(v.VehicleId, id, patch, Source, cancellationToken);
        return ToolHelpers.ToResult(result, $"Updated \"{result.Value?.Name ?? "item"}\" on {v.Registration}.");
    }

    [McpServerTool(Name = "delete_equipment")]
    [Description("Delete an equipment/kit item (id from list_equipment).")]
    public static async Task<McpResult<DeletedRow>> DeleteEquipment(
        VehicleResolver resolver,
        LogWriteService writes,
        [Description("The item's id (from list_equipment).")] int id,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var result = await writes.DeleteEquipmentAsync(v.VehicleId, id, Source, cancellationToken);
        return ToDeleteResult(result, id, $"Deleted equipment item {id} from {v.Registration}.");
    }

    /// <summary>Maps a delete <see cref="WriteResult{Boolean}"/> to a tool result: a clean id echo, or an McpException.</summary>
    private static McpResult<DeletedRow> ToDeleteResult(WriteResult<bool> result, int id, string success) =>
        result.Status switch
        {
            WriteStatus.Updated => new McpResult<DeletedRow>(success, new DeletedRow(id)),
            WriteStatus.Conflict => throw new McpException($"{result.ConflictTitle}: {result.ConflictDetail}"),
            _ => throw new McpException("The item was not found. Use the matching list_* tool to find the id."),
        };

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

/// <summary>The id of a row a delete tool removed.</summary>
public sealed record DeletedRow(int Id);
