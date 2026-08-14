using System.Globalization;
using GymLink.Application.Reporting;
using GymLink.Domain.Enums;
using GymLink.Domain.Trainers;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace GymLink.Infrastructure.Reporting;

internal sealed class QuestPdfReportRenderer : IReportPdfRenderer
{
    private static readonly CultureInfo BosnianCulture = CultureInfo.GetCultureInfo("bs-BA");
    private static readonly TimeZoneInfo SarajevoTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
        TrainerAvailabilitySchedule.SarajevoTimeZoneId);

    static QuestPdfReportRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Render(MembershipReportDocument document) => Document.Create(container =>
        container.Page(page =>
        {
            ConfigurePage(page, "Izvještaj o članstvima", document.Window);
            page.Content().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn(1.5f);
                    columns.RelativeColumn(1.1f);
                    columns.RelativeColumn(0.9f);
                    columns.RelativeColumn(1f);
                    columns.RelativeColumn(1f);
                });
                Header(table, "Teretana", "Član", "Plan", "Status", "Početak", "Kraj");
                if (document.Rows.Count == 0)
                {
                    table.Cell().ColumnSpan(6).PaddingVertical(18)
                        .AlignCenter().Text("Nema članstava u izvještajnom periodu.")
                        .FontColor(Colors.Grey.Darken1);
                }
                foreach (var row in document.Rows)
                {
                    Cell(table, row.GymName);
                    Cell(table, row.MemberName);
                    Cell(table, row.PlanName);
                    Cell(table, row.Status.ToString());
                    Cell(table, LocalDate(row.StartsAtUtc));
                    Cell(table, row.EndsAtUtc.HasValue ? LocalDate(row.EndsAtUtc.Value) : "—");
                }
            });
            ConfigureFooter(page, document.Rows.Count);
        })).GeneratePdf();

    public byte[] Render(ReservationReportDocument document) => Document.Create(container =>
        container.Page(page =>
        {
            ConfigurePage(page, "Izvještaj o rezervacijama", document.Window);
            page.Content().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1.1f);
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn(0.9f);
                    columns.RelativeColumn(0.9f);
                });
                Header(
                    table,
                    "Teretana",
                    "Član",
                    "Trener",
                    "Usluga",
                    "Termin",
                    "Status",
                    "Plaćanje");
                if (document.Rows.Count == 0)
                {
                    table.Cell().ColumnSpan(7).PaddingVertical(18)
                        .AlignCenter().Text("Nema rezervacija u izvještajnom periodu.")
                        .FontColor(Colors.Grey.Darken1);
                }
                foreach (var row in document.Rows)
                {
                    Cell(table, row.GymName);
                    Cell(table, row.MemberName);
                    Cell(table, row.TrainerName);
                    Cell(table, row.OfferingName);
                    Cell(table, LocalDateTime(row.StartsAtUtc));
                    Cell(table, row.Status.ToString());
                    Cell(table, ReservationPaymentMethodLabel(row.PaymentMethod));
                }
            });
            ConfigureFooter(page, document.Rows.Count);
        })).GeneratePdf();

    internal static string ReservationPaymentMethodLabel(
        ReservationPaymentMethod paymentMethod) => paymentMethod switch
        {
            ReservationPaymentMethod.Stripe => "Online",
            ReservationPaymentMethod.PayInPerson => "Uživo",
            _ => throw new ArgumentOutOfRangeException(
                nameof(paymentMethod),
                paymentMethod,
                "Unsupported reservation payment method."),
        };

    private static void ConfigurePage(
        PageDescriptor page,
        string title,
        ReportingWindow window)
    {
        page.Size(PageSizes.A4.Landscape());
        page.Margin(28);
        page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(9));
        page.Header().PaddingBottom(14).Column(column =>
        {
            column.Item().Text("GymLink").FontSize(11).FontColor(Colors.Blue.Medium);
            column.Item().Text(title).FontSize(22).SemiBold();
            column.Item().PaddingTop(4).Text(
                    $"Period: {window.WindowStart:dd.MM.yyyy} – " +
                    $"{window.WindowEnd:dd.MM.yyyy} · Vremenska zona: {window.TimeZone}")
                .FontColor(Colors.Grey.Darken1);
            column.Item().Text(
                    $"Generisano: {LocalDateTime(window.GeneratedAtUtc)}")
                .FontColor(Colors.Grey.Darken1);
        });
    }

    private static void ConfigureFooter(PageDescriptor page, int recordCount) =>
        page.Footer().PaddingTop(10).Row(row =>
        {
            row.RelativeItem().Text($"Ukupno zapisa: {recordCount}");
            row.RelativeItem().AlignRight().Text(text =>
            {
                text.Span("Stranica ");
                text.CurrentPageNumber();
                text.Span(" od ");
                text.TotalPages();
            });
        });

    private static void Header(TableDescriptor table, params string[] values)
    {
        foreach (var value in values)
        {
            table.Cell().Background(Colors.Blue.Darken2).Padding(6)
                .Text(value).SemiBold().FontColor(Colors.White);
        }
    }

    private static void Cell(TableDescriptor table, string value) =>
        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6)
            .Text(value);

    private static string LocalDate(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(utc), SarajevoTimeZone)
            .ToString("dd.MM.yyyy", BosnianCulture);

    private static string LocalDateTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(utc), SarajevoTimeZone)
            .ToString("dd.MM.yyyy HH:mm", BosnianCulture);

    private static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
