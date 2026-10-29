package my.webgso;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.time.format.DateTimeFormatter;
import java.util.*;
import my.webgso.Models.LssPlant;
import org.apache.commons.csv.*;

/** Export the same latest-per-API results shown in History, with Excel-safe text cells. */
public final class ApiHistoryCsv {
  private ApiHistoryCsv() {}

  static String safeText(String value) {
    if (value == null) return "";
    String trimmed = value.stripLeading();
    return (!trimmed.isEmpty() && "=+-@".indexOf(trimmed.charAt(0)) >= 0)
            || value.startsWith("\t")
            || value.startsWith("\r")
            || value.startsWith("\n")
        ? "'" + value
        : value;
  }

  public static byte[] write(
      List<ApiTestHistoryService.Entry> entries, Map<Integer, LssPlant> plants) throws IOException {
    var text = new StringWriter();
    text.write('\ufeff');
    try (var csv = new CSVPrinter(text, CSVFormat.DEFAULT)) {
      csv.printRecord(
          "Tested at (MYT)",
          "Plant ID",
          "Plant name",
          "API",
          "Tested by",
          "Result",
          "HTTP status",
          "Response time (ms)",
          "Test duration (ms)",
          "Message");
      for (var entry : entries) {
        var plant = plants.get(entry.plantId());
        csv.printRecord(
            entry
                .testedAt()
                .atZoneSameInstant(Policies.LOCAL)
                .format(DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm:ss")),
            safeText(plant == null ? Integer.toString(entry.plantId()) : plant.plantId),
            safeText(plant == null ? "Deleted plant" : plant.name),
            safeText(entry.dataset()),
            safeText(entry.actor()),
            safeText(entry.outcome()),
            entry.httpStatus(),
            entry.responseMillis(),
            entry.durationMillis(),
            safeText(entry.message()));
      }
    }
    return text.toString().getBytes(StandardCharsets.UTF_8);
  }
}
