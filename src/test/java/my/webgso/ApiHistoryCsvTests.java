package my.webgso;

import static org.assertj.core.api.Assertions.assertThat;

import java.nio.charset.StandardCharsets;
import java.time.OffsetDateTime;
import java.util.*;
import my.webgso.Models.LssPlant;
import org.apache.commons.csv.*;
import org.junit.jupiter.api.Test;

class ApiHistoryCsvTests {
  @Test
  void quotesMultilineCellsUsesMytAndEscapesSpreadsheetFormulas() throws Exception {
    var plant = new LssPlant();
    plant.plantId = "01";
    plant.name = "=1+1";
    var entry =
        new ApiTestHistoryService.Entry(
            1,
            1,
            "Day Ahead",
            OffsetDateTime.parse("2026-10-29T16:05:00Z"),
            "admin",
            "Failed",
            null,
            null,
            12,
            "Line one, quoted \"value\"\nLine two");
    String csv =
        new String(ApiHistoryCsv.write(List.of(entry), Map.of(1, plant)), StandardCharsets.UTF_8);
    try (var parser =
        CSVParser.parse(
            csv.substring(1),
            CSVFormat.DEFAULT.builder().setHeader().setSkipHeaderRecord(true).get())) {
      var records = parser.getRecords();
      assertThat(records).hasSize(1);
      assertThat(records.getFirst().get("Tested at (MYT)")).isEqualTo("2026-10-30 00:05:00");
      assertThat(records.getFirst().get("Plant name")).isEqualTo("'=1+1");
      assertThat(records.getFirst().get("Message")).isEqualTo(entry.message());
    }
    for (String value : List.of("=1+1", " +1", "-1+2", "@SUM(A1)", "\tcommand", "\ncommand"))
      assertThat(ApiHistoryCsv.safeText(value)).startsWith("'");
    assertThat(ApiHistoryCsv.safeText("Normal result")).isEqualTo("Normal result");
  }
}
