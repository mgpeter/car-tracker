using System.Text;
using System.Text.Json;
using CarTracker.Domain.Accounts;
using CarTracker.Domain.Accounts.Import;
using CarTracker.Shared;

namespace CarTracker.Domain.Tests;

/// <summary>
/// Reading an export back: what parses, what is refused, and what is refused <i>by name</i>.
/// </summary>
/// <remarks>
/// <para>
/// The whole reason these are worth writing is asymmetry. <see cref="JsonSerializer"/> is generous in one
/// direction and silent in the other: an unknown property is ignored, which is what lets a file written by a
/// later version still import - and an <b>absent</b> property is filled with <c>default</c> with nothing said,
/// which is what would turn a half-downloaded file into a garage of cars whose odometers all read zero. So
/// the tolerance has to be proved and the silence has to be closed, and they are two different tests.
/// </para>
/// <para>
/// Nothing here touches a database. Reading a file is a question about the file.
/// </para>
/// </remarks>
public class ImportReaderTests
{
    private static Task<ImportReadResult> ReadAsync(string json) =>
        ImportReader.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    /// <summary>A minimal file of the shape the export writes, with one car and one fill.</summary>
    private const string OneCar = """
        {
          "exportedAt": "2026-08-14T19:02:11+00:00",
          "schemaVersion": "0.18.0",
          "notes": ["prose for a human reader"],
          "account": { "externalId": "auth0|abc", "email": "someone@example.test", "displayName": null,
                       "createdAt": "2026-03-01T00:00:00+00:00" },
          "reference": {
            "garages": [{ "name": "K & P Motors", "contact": null, "address": null, "notes": null }],
            "washLocations": [],
            "expenseCategories": [{ "name": "Fuel", "displayOrder": 1, "isSystem": true }]
          },
          "vehicles": [
            {
              "registration": "BT53 AKJ",
              "profile": {
                "id": 1, "ownerId": 4, "registration": "BT53 AKJ", "make": "Land Rover", "model": "Freelander 1",
                "year": 2003, "purchaseDate": "2026-03-14", "purchaseMileage": 76632, "purchasePrice": 1700.00,
                "fuelType": "Petrol", "status": "Active", "isDefault": true,
                "fluids": { "oilSpec": "5W-30", "fuelTankCapacityLitres": 59.0 },
                "tyres": { "tyreSize": "215/65 R16" },
                "insurance": { "insurer": "Adrian Flux" },
                "breakdown": { "provider": "RAC" },
                "source": "Web"
              },
              "fuelEntries": [
                { "id": 9, "entryDate": "2026-04-02", "mileage": 77881, "litres": 44.02,
                  "pricePerLitre": 1.599, "totalCost": 70.39, "station": "Applegreen",
                  "fillLevel": "Half", "notes": null }
              ],
              "expenses": [
                { "id": 21, "entryDate": "2026-04-02", "category": "Fuel", "amount": 70.39,
                  "fuelEntryId": 9, "isVehiclePurchase": false }
              ]
            }
          ],
          "assistantTokens": [],
          "assistantWriteAudit": []
        }
        """;

    [Fact]
    public async Task Reads_an_export_into_its_own_shapes()
    {
        var result = await ReadAsync(OneCar);

        Assert.Equal(ImportReadOutcome.Ok, result.Outcome);
        var payload = Assert.IsType<ImportPayload>(result.Payload);

        Assert.Equal("0.18.0", payload.SchemaVersion);
        Assert.Equal(new DateTimeOffset(2026, 8, 14, 19, 2, 11, TimeSpan.Zero), payload.ExportedAt);
        Assert.Equal("someone@example.test", payload.Account?.Email);

        var vehicle = Assert.Single(payload.Vehicles);
        Assert.Equal("BT53 AKJ", vehicle.Plate);

        // The profile is the entity, so the owned blocks and the enums come back as themselves rather than as
        // whatever a hand-written reader's projection remembered to carry.
        Assert.Equal(FuelType.Petrol, vehicle.Profile!.FuelType);
        Assert.Equal(76_632, vehicle.Profile.PurchaseMileage);
        Assert.Equal(59.0m, vehicle.Profile.Fluids.FuelTankCapacityLitres);
        Assert.Equal("215/65 R16", vehicle.Profile.Tyres.TyreSize);
        Assert.Equal("Adrian Flux", vehicle.Profile.Insurance.Insurer);
        Assert.Equal("RAC", vehicle.Profile.Breakdown.Provider);

        Assert.Equal(FillLevel.Half, Assert.Single(vehicle.FuelEntries).FillLevel);
        Assert.Equal(9, Assert.Single(vehicle.Expenses).FuelEntryId);
        Assert.Equal("K & P Motors", Assert.Single(payload.Reference.Garages).Name);
    }

    /// <summary>
    /// A list the file does not carry is an empty list, never null - the sixteen call sites downstream ask no
    /// questions, and an absent array and an empty one mean the same thing to an import.
    /// </summary>
    [Fact]
    public async Task Absent_arrays_read_as_empty_rather_than_null()
    {
        var payload = (await ReadAsync(OneCar)).Payload!;
        var vehicle = payload.Vehicles[0];

        Assert.Empty(vehicle.MileageReadings);
        Assert.Empty(vehicle.CheckDefinitions);
        Assert.Empty(vehicle.BudgetGroups);
        Assert.Empty(vehicle.Documents);
        Assert.Empty(payload.Reference.WashLocations);
    }

    /// <summary>
    /// A file from a later release must still import, because refusing a version mismatch would break every
    /// import on every release. The preview warns about it; the reader does not object.
    /// </summary>
    [Fact]
    public async Task Ignores_properties_it_does_not_know()
    {
        var withFutureFields = OneCar
            .Replace("\"schemaVersion\": \"0.18.0\",", "\"schemaVersion\": \"99.0.0\", \"greenLaneTrips\": [{\"id\": 1}],")
            .Replace("\"station\": \"Applegreen\",", "\"station\": \"Applegreen\", \"forecourtBrand\": \"Circle K\",");

        var result = await ReadAsync(withFutureFields);

        Assert.Equal(ImportReadOutcome.Ok, result.Outcome);
        Assert.Equal("99.0.0", result.Payload!.SchemaVersion);
        Assert.Equal("Applegreen", result.Payload.Vehicles[0].FuelEntries[0].Station);
    }

    [Fact]
    public async Task Refuses_a_file_that_is_not_json()
    {
        var result = await ReadAsync("registration,make,model\nBT53 AKJ,Land Rover,Freelander 1\n");

        Assert.Equal(ImportReadOutcome.Unreadable, result.Outcome);
        Assert.Null(result.Payload);
        Assert.Contains("not readable JSON", result.Detail);
    }

    /// <summary>
    /// The interrupted download. It is valid JSON right up to the point it stops, which is exactly why the
    /// parser's own message - naming a line - is passed through rather than paraphrased.
    /// </summary>
    [Fact]
    public async Task Refuses_a_truncated_file_and_names_the_parse_failure()
    {
        var result = await ReadAsync(OneCar[..(OneCar.Length / 2)]);

        Assert.Equal(ImportReadOutcome.Unreadable, result.Outcome);
        Assert.Null(result.Payload);
        Assert.Contains("not readable JSON", result.Detail);
    }

    /// <summary>
    /// Readable JSON is not the same thing as one of ours. Without this an unrelated document deserialises
    /// into a payload of nulls, and the empty-list normalisation turns that into a cheerful "nothing to do".
    /// </summary>
    [Fact]
    public async Task Refuses_json_that_is_not_an_export_of_this_app()
    {
        var result = await ReadAsync("""{ "name": "shopping list", "items": ["milk"] }""");

        Assert.Equal(ImportReadOutcome.Unreadable, result.Outcome);
        Assert.Contains("does not look like a cambelt.app account export", result.Detail);
    }

    [Fact]
    public async Task Refuses_an_empty_file()
    {
        var result = await ReadAsync(string.Empty);

        Assert.Equal(ImportReadOutcome.Unreadable, result.Outcome);
        Assert.Contains("empty", result.Detail);
    }

    /// <summary>
    /// An account with no cars is a real export and imports to nothing. It is distinguishable from an
    /// unrelated file by carrying an export date, which is the reason the reader asks for both.
    /// </summary>
    [Fact]
    public async Task Accepts_an_export_of_an_account_with_no_vehicles()
    {
        var result = await ReadAsync("""
            { "exportedAt": "2026-08-14T19:02:11+00:00", "schemaVersion": "0.18.0", "vehicles": [] }
            """);

        Assert.Equal(ImportReadOutcome.Ok, result.Outcome);
        Assert.Empty(result.Payload!.Vehicles);
    }

    /// <summary>
    /// The cap is enforced while reading, not from a header, because a header is a claim by the client and the
    /// point of a cap is the case where the client is wrong. Nothing is parsed on the way to refusing.
    /// </summary>
    [Fact]
    public async Task Refuses_a_file_over_the_cap_without_parsing_it()
    {
        var oversize = new PaddingStream(ImportReader.MaxSizeBytes + 4096);

        var result = await ImportReader.ReadAsync(oversize);

        Assert.Equal(ImportReadOutcome.TooLarge, result.Outcome);
        Assert.Null(result.Payload);
        Assert.Contains("25 MB", result.Detail);
    }

    [Fact]
    public async Task Accepts_a_file_at_exactly_the_cap()
    {
        // Padding inside a note keeps it a valid export while making it exactly as large as the limit allows.
        var padding = new string('x', (int)ImportReader.MaxSizeBytes - 200);
        var json = $$"""
            { "exportedAt": "2026-08-14T19:02:11+00:00", "schemaVersion": "0.18.0", "vehicles": [],
              "notes": ["{{padding}}"] }
            """;
        var bytes = Encoding.UTF8.GetBytes(json);
        Assert.InRange(bytes.Length, ImportReader.MaxSizeBytes - 300, ImportReader.MaxSizeBytes);

        var result = await ImportReader.ReadAsync(new MemoryStream(bytes));

        Assert.Equal(ImportReadOutcome.Ok, result.Outcome);
    }

    /// <summary>The export's own serializer settings, both ways, so a round trip cannot disagree with itself.</summary>
    [Fact]
    public void Reads_with_the_settings_the_export_writes_with()
    {
        Assert.Contains(AccountExportService.Json.Converters,
            c => c is System.Text.Json.Serialization.JsonStringEnumConverter);
        Assert.Equal(JsonNamingPolicy.CamelCase, AccountExportService.Json.PropertyNamingPolicy);
    }

    /// <summary>
    /// Bytes without a length header worth trusting: <see cref="Length"/> is never consulted by the reader,
    /// and the content is spaces, so an oversize refusal cannot be coming from a failed parse.
    /// </summary>
    private sealed class PaddingStream(long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= length) return 0;
            var read = (int)Math.Min(count, length - _position);
            Array.Fill(buffer, (byte)' ', offset, read);
            _position += read;
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
