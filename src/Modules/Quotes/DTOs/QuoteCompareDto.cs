namespace saas.Modules.Quotes.DTOs;
using System;

public class QuoteCompareDto
{
    public Guid QuoteId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public int Version1Number { get; set; }
    public int Version2Number { get; set; }
    public QuotePreviewDto Version1Preview { get; set; } = new();
    public QuotePreviewDto Version2Preview { get; set; } = new();
}