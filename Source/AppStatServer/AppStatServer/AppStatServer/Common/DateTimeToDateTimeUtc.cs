using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AppStatServer.Common;

public class DateTimeToDateTimeUtc()
    : ValueConverter<DateTime, DateTime>(c => DateTime.SpecifyKind(c, DateTimeKind.Utc), c => c);