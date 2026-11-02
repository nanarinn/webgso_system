package my.webgso;

import static org.assertj.core.api.Assertions.*;

import java.nio.file.*;
import java.time.*;
import java.time.format.DateTimeFormatter;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import org.springframework.mock.env.MockEnvironment;

class DatabaseBackupsTests {
  @TempDir Path dir;

  @Test
  void rotationKeepsSevenNewestAndNeverDeletesPartialOrUnrelatedFiles() throws Exception {
    var b = new DatabaseBackups(new MockEnvironment());
    for (int n = 1; n <= 9; n++)
      Files.writeString(
          dir.resolve("gso-202610" + String.format("%02d", n) + "-020000.dump"), "archive");
    Files.writeString(dir.resolve("gso-20260901-020000.dump.partial"), "incomplete");
    Files.writeString(dir.resolve("personal.dump"), "keep");
    b.rotate(dir);
    assertThat(Files.exists(dir.resolve("gso-20261001-020000.dump"))).isFalse();
    assertThat(Files.exists(dir.resolve("gso-20261003-020000.dump"))).isTrue();
    assertThat(Files.exists(dir.resolve("gso-20261009-020000.dump"))).isTrue();
    assertThat(Files.readString(dir.resolve("personal.dump"))).isEqualTo("keep");
    assertThat(Files.exists(dir.resolve("gso-20260901-020000.dump.partial"))).isTrue();
    try (var files = Files.list(dir)) {
      assertThat(files.filter(p -> DatabaseBackups.archive(p.getFileName().toString())).count())
          .isEqualTo(7);
    }
  }

  @Test
  void disabledBackupNeverCreatesFiles() throws Exception {
    var b =
        new DatabaseBackups(
            new MockEnvironment()
                .withProperty("webgso.backup.directory", dir.resolve("unused").toString()));
    b.daily();
    assertThat(b.status()).contains("disabled");
    assertThat(Files.exists(dir.resolve("unused"))).isFalse();
  }

  @Test
  void completedDailyBackupAvoidsDumpAndIgnoresUnrelatedFiles() throws Exception {
    var b =
        new DatabaseBackups(
            new MockEnvironment()
                .withProperty("webgso.backup.enabled", "true")
                .withProperty("spring.datasource.url", "jdbc:postgresql://localhost/test")
                .withProperty("webgso.backup.directory", dir.toString()));
    String today = LocalDate.now(Policies.LOCAL).format(DateTimeFormatter.BASIC_ISO_DATE);
    Files.writeString(dir.resolve("gso-" + today + "-020000.dump"), "archive");
    Files.writeString(dir.resolve("notes.dump"), "keep");
    b.daily();
    assertThat(b.status()).contains("up to date", "1 local copies");
    assertThat(Files.readString(dir.resolve("notes.dump"))).isEqualTo("keep");
  }

  @Test
  void failedDumpPreservesExistingBackupsAndCleansPartialFile() throws Exception {
    Files.writeString(dir.resolve("gso-20000101-020000.dump"), "archive");
    var b =
        new DatabaseBackups(
            new MockEnvironment()
                .withProperty("webgso.backup.enabled", "true")
                .withProperty("spring.datasource.url", "jdbc:postgresql://localhost/test")
                .withProperty("webgso.backup.directory", dir.toString())
                .withProperty("webgso.backup.pg-dump", dir.resolve("missing-program").toString()));
    b.daily();
    assertThat(b.status()).contains("Backup failed");
    assertThat(Files.exists(dir.resolve("gso-20000101-020000.dump"))).isTrue();
    try (var files = Files.list(dir)) {
      assertThat(files.noneMatch(p -> p.toString().endsWith("partial"))).isTrue();
    }
  }
}
