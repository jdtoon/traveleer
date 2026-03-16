import sys
content = open('src/Modules/Quotes/Services/QuoteService.cs', 'r', encoding='utf-8').read()

code_to_insert = """
    public async Task<QuoteCompareDto?> CompareVersionsAsync(Guid id, Guid version1Id, Guid version2Id)
    {
        var versions = await _db.QuoteVersions
            .AsNoTracking()
            .Include(x => x.Quote)
            .Where(x => x.QuoteId == id && (x.Id == version1Id || x.Id == version2Id))
            .ToListAsync();

        if (versions.Count != 2) return null;

        var v1 = versions.First(x => x.Id == version1Id);
        var v2 = versions.First(x => x.Id == version2Id);

        var snap1 = JsonSerializer.Deserialize<QuoteVersionSnapshotDto>(v1.SnapshotJson, SnapshotJsonOptions) ?? new();
        var snap2 = JsonSerializer.Deserialize<QuoteVersionSnapshotDto>(v2.SnapshotJson, SnapshotJsonOptions) ?? new();

        var builder1 = new QuoteBuilderDto
        {
            ClientName = snap1.ClientName,
            OutputCurrencyCode = string.IsNullOrWhiteSpace(snap1.OutputCurrencyCode) ? "USD" : snap1.OutputCurrencyCode,
            MarkupPercentage = snap1.MarkupPercentage,
            TemplateLayout = snap1.TemplateLayout,
            GroupBy = snap1.GroupBy,
            ShowImages = snap1.ShowImages,
            ShowMealPlan = snap1.ShowMealPlan,
            ShowFooter = snap1.ShowFooter,
            ShowRoomDescriptions = snap1.ShowRoomDescriptions,
            ValidUntil = snap1.ValidUntil,
            TravelStartDate = snap1.TravelStartDate,
            TravelEndDate = snap1.TravelEndDate,
            FilterByTravelDates = snap1.FilterByTravelDates,
            Notes = snap1.Notes,
            InternalNotes = snap1.InternalNotes,
            SelectedRateCardIds = snap1.SelectedRateCardIds,
        };

        var builder2 = new QuoteBuilderDto
        {
            ClientName = snap2.ClientName,
            OutputCurrencyCode = string.IsNullOrWhiteSpace(snap2.OutputCurrencyCode) ? "USD" : snap2.OutputCurrencyCode,
            MarkupPercentage = snap2.MarkupPercentage,
            TemplateLayout = snap2.TemplateLayout,
            GroupBy = snap2.GroupBy,
            ShowImages = snap2.ShowImages,
            ShowMealPlan = snap2.ShowMealPlan,
            ShowFooter = snap2.ShowFooter,
            ShowRoomDescriptions = snap2.ShowRoomDescriptions,
            ValidUntil = snap2.ValidUntil,
            TravelStartDate = snap2.TravelStartDate,
            TravelEndDate = snap2.TravelEndDate,
            FilterByTravelDates = snap2.FilterByTravelDates,
            Notes = snap2.Notes,
            InternalNotes = snap2.InternalNotes,
            SelectedRateCardIds = snap2.SelectedRateCardIds,
        };

        var preview1 = await BuildPreviewAsync(builder1);
        var preview2 = await BuildPreviewAsync(builder2);

        return new QuoteCompareDto
        {
            QuoteId = id,
            ReferenceNumber = v1.Quote?.ReferenceNumber ?? string.Empty,
            Version1Number = v1.VersionNumber,
            Version2Number = v2.VersionNumber,
            Version1Preview = preview1,
            Version2Preview = preview2
        };
    }
"""

content = content.replace("public async Task<QuoteVersionDetailsDto?> GetVersionDetailsAsync(Guid id, Guid versionId)", code_to_insert + "\n    public async Task<QuoteVersionDetailsDto?> GetVersionDetailsAsync(Guid id, Guid versionId)")

open('src/Modules/Quotes/Services/QuoteService.cs', 'w', encoding='utf-8').write(content)
