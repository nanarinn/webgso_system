package my.webgso;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import my.webgso.Models.SolarApiReading;
import org.apache.commons.csv.*;

/** CSV of the complete filtered Analytics result, rather than just the visible page. */
public final class AnalyticsCsv {
  private AnalyticsCsv() {}

  public static byte[] write(List<SolarApiReading> rows, Map<Integer, String> plants)
      throws IOException {
    var text = new StringWriter();
    text.write('\ufeff');
    try (var csv = new CSVPrinter(text, CSVFormat.DEFAULT)) {
      csv.printRecord(
          "Timestamp (MYT)",
          "Plant",
          "Submission (MYT)",
          "Parameter",
          "Value",
          "Unit",
          "Interval (minutes)");
      for (var r : rows)
        csv.printRecord(
            r.intervalStart,
            ApiHistoryCsv.safeText(plants.getOrDefault(r.plantId, Integer.toString(r.plantId))),
            r.submissionDateTime,
            ApiHistoryCsv.safeText(r.parameter),
            r.value,
            ApiHistoryCsv.safeText(r.unit),
            r.intervalMinutes);
    }
    return text.toString().getBytes(StandardCharsets.UTF_8);
  }
}
