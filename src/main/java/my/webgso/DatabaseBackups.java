package my.webgso;

import java.io.*;
import java.net.URI;
import java.nio.channels.*;
import java.nio.file.*;
import java.nio.file.attribute.PosixFilePermissions;
import java.time.*;
import java.time.format.DateTimeFormatter;
import java.util.*;
import java.util.concurrent.TimeUnit;
import org.springframework.core.env.Environment;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Service;

/** Local pg_dump archives. Never restores, emails or exposes backup contents via HTTP. */
@Service
public class DatabaseBackups {
  private final Environment env;
  private volatile String result = "Waiting for the first daily backup.";

  public DatabaseBackups(Environment env) {
    this.env = env;
  }

  Path directory() {
    return Path.of(env.getProperty("webgso.backup.directory", ".tools/database-backups"))
        .toAbsolutePath()
        .normalize();
  }

  static boolean archive(String name) {
    return name.matches("gso-[0-9]{8}-[0-9]{6}\\.dump");
  }

  private List<Path> archives(Path dir) throws IOException {
    if (!Files.isDirectory(dir, LinkOption.NOFOLLOW_LINKS)) return List.of();
    try (var files = Files.list(dir)) {
      return files
          .filter(
              p ->
                  archive(p.getFileName().toString())
                      && Files.isRegularFile(p, LinkOption.NOFOLLOW_LINKS))
          .sorted(Comparator.comparing((Path p) -> p.getFileName().toString()).reversed())
          .toList();
    }
  }

  public String status() {
    if (!env.getProperty("webgso.backup.enabled", Boolean.class, false))
      return "Automatic backup is disabled on this server.";
    try {
      var files = archives(directory());
      if (!files.isEmpty())
        return result
            + " Latest completed backup: "
            + files.getFirst().getFileName()
            + " (MYT). "
            + files.size()
            + " local copies.";
    } catch (IOException ignored) {
      return "Backup status unavailable. Ask the server maintainer to check backup storage.";
    }
    return result;
  }

  void rotate(Path dir) throws IOException {
    var normalized = dir.toAbsolutePath().normalize();
    for (var old : archives(normalized).stream().skip(7).toList())
      if (old.getParent().equals(normalized) && archive(old.getFileName().toString()))
        Files.delete(old);
  }

  @Scheduled(fixedDelay = 300000, initialDelay = 60000)
  public synchronized void daily() {
    if (!env.getProperty("webgso.backup.enabled", Boolean.class, false)) return;
    Path partial = null;
    try {
      var url = env.getProperty("spring.datasource.url", "");
      if (!url.startsWith("jdbc:postgresql://")) {
        result = "Backup requires PostgreSQL.";
        return;
      }
      Path dir = directory();
      Files.createDirectories(dir);
      if (Files.isSymbolicLink(dir)) throw new IOException("Backup directory is a symbolic link");
      try {
        Files.setPosixFilePermissions(dir, PosixFilePermissions.fromString("rwx------"));
      } catch (UnsupportedOperationException ignored) {
      }
      // A file lock prevents overlapping pg_dump runs across application instances.
      try (var channel =
              FileChannel.open(
                  dir.resolve("backup.lock"), StandardOpenOption.CREATE, StandardOpenOption.WRITE);
          var lock = channel.tryLock()) {
        if (lock == null) return;
        String today = LocalDate.now(Policies.LOCAL).format(DateTimeFormatter.BASIC_ISO_DATE);
        if (archives(dir).stream()
            .anyMatch(p -> p.getFileName().toString().startsWith("gso-" + today + "-"))) {
          result = "Daily backup is up to date.";
          return;
        }
        var uri = URI.create(url.substring(5));
        if (uri.getHost() == null || uri.getUserInfo() != null)
          throw new IOException("Invalid database host");
        String filename =
            "gso-"
                + LocalDateTime.now(Policies.LOCAL)
                    .format(DateTimeFormatter.ofPattern("yyyyMMdd-HHmmss"))
                + ".dump";
        Path target = dir.resolve(filename);
        partial = dir.resolve(filename + ".partial");
        String defaultDump =
            System.getProperty("os.name").startsWith("Windows")
                ? "C:/Program Files/PostgreSQL/18/bin/pg_dump.exe"
                : "pg_dump";
        String dump = env.getProperty("webgso.backup.pg-dump", defaultDump);
        var builder =
            new ProcessBuilder(
                dump,
                "--host=" + uri.getHost(),
                "--port=" + (uri.getPort() < 0 ? 5432 : uri.getPort()),
                "--username=" + env.getProperty("spring.datasource.username", ""),
                "--dbname=" + uri.getPath().substring(1),
                "--format=custom",
                "--no-password",
                "--file=" + partial);
        builder.environment().put("PGPASSWORD", env.getProperty("spring.datasource.password", ""));
        builder.environment().put("PGCONNECT_TIMEOUT", "10");
        builder.redirectOutput(ProcessBuilder.Redirect.DISCARD);
        builder.redirectError(ProcessBuilder.Redirect.DISCARD);
        result = "Creating today's database backup.";
        Process process = builder.start();
        try {
          if (!process.waitFor(120, TimeUnit.SECONDS)) {
            process.destroyForcibly().waitFor();
            throw new IOException("Backup timed out");
          }
          if (process.exitValue() != 0 || !Files.isRegularFile(partial) || Files.size(partial) == 0)
            throw new IOException("pg_dump failed");
        } finally {
          if (process.isAlive()) process.destroyForcibly();
          builder.environment().remove("PGPASSWORD");
        }
        try {
          Files.move(partial, target, StandardCopyOption.ATOMIC_MOVE);
        } catch (AtomicMoveNotSupportedException ignored) {
          Files.move(partial, target);
        }
        partial = null;
        rotate(dir);
        result = "Daily backup completed successfully.";
      }
    } catch (Exception e) {
      if (e instanceof InterruptedException) Thread.currentThread().interrupt();
      result =
          "Backup failed. Check pg_dump, database access and disk space on the server. The next"
              + " check is in five minutes.";
      if (partial != null)
        try {
          Files.deleteIfExists(partial);
        } catch (IOException ignored) {
        }
    }
  }
}
