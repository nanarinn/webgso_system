package my.webgso.ui;

import static org.assertj.core.api.Assertions.assertThat;

import java.time.OffsetDateTime;
import java.util.Map;
import my.webgso.DatabaseConnectionStatus;
import org.junit.jupiter.api.Test;

class DatabaseStatusCardTests {
  @Test
  void distinguishesConnectedDisconnectedAndMissingChecks() {
    for (boolean connected : new boolean[] {true, false}) {
      String html =
          DatabaseStatusCard.render(
                  new PageContext(
                      Map.of(
                          "databaseConnection",
                          new DatabaseConnectionStatus.Snapshot(
                              connected, OffsetDateTime.parse("2026-10-29T04:00:00Z"))),
                      null))
              .render();
      assertThat(html).contains(connected ? "Database connected" : "Database disconnected");
      assertThat(html).contains("Checked 12:00:00 MYT", "role=\"status\"");
    }
    assertThat(DatabaseStatusCard.render(new PageContext(Map.of(), null)).render())
        .contains("Database status unavailable")
        .doesNotContain("Database connected");
  }
}
