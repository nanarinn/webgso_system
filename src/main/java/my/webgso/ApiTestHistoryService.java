package my.webgso;

import jakarta.annotation.PostConstruct;
import java.time.*;
import java.util.*;
import javax.sql.DataSource;
import org.springframework.core.io.ClassPathResource;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.jdbc.datasource.init.ResourceDatabasePopulator;
import org.springframework.stereotype.Service;

@Service
public class ApiTestHistoryService {
  public record Entry(
      long id,
      int plantId,
      String dataset,
      OffsetDateTime testedAt,
      String actor,
      String outcome,
      Integer httpStatus,
      Long responseMillis,
      long durationMillis,
      String message) {}

  private final DataSource source;
  private final ApiEmailAlerts alerts;
  private final JdbcTemplate jdbc;

  public ApiTestHistoryService(DataSource source, ApiEmailAlerts alerts) {
    this.alerts = alerts;
    this.source = source;
    this.jdbc = new JdbcTemplate(source);
  }

  @PostConstruct
  void initialize() {
    new ResourceDatabasePopulator(new ClassPathResource("schema-api-test-history.sql"))
        .execute(source);
    jdbc.update(
        "DELETE FROM \"ApiTestClaims\" WHERE EXISTS (SELECT 1 FROM \"ApiTestHistory\" h WHERE"
            + " h.\"PlantId\"=\"ApiTestClaims\".\"PlantId\" AND"
            + " h.\"Dataset\"=\"ApiTestClaims\".\"Dataset\" AND"
            + " h.\"TestedAt\">=\"ApiTestClaims\".\"StartedAt\")");
  }

  public static String key(int plantId, String dataset) {
    return plantId + ":" + Policies.canonical(dataset);
  }

  // Called while holding the plant row lock, before making any outbound request.
  public boolean claim(int plantId, String dataset) {
    if (jdbc.queryForObject(
            "SELECT COUNT(*) FROM \"ApiTestClaims\" WHERE \"PlantId\"=? AND \"Dataset\"=?",
            Long.class,
            plantId,
            dataset)
        > 0) return false;
    jdbc.update(
        "INSERT INTO \"ApiTestClaims\" (\"PlantId\",\"Dataset\",\"StartedAt\") VALUES (?,?,?)",
        plantId,
        dataset,
        OffsetDateTime.now(ZoneOffset.UTC));
    return true;
  }

  public void release(int plantId, String dataset) {
    jdbc.update(
        "DELETE FROM \"ApiTestClaims\" WHERE \"PlantId\"=? AND \"Dataset\"=?", plantId, dataset);
  }

  public static final class AlreadyTested extends IllegalArgumentException {
    public AlreadyTested() {
      this("This API is currently being tested. Please wait for it to finish.");
    }

    public AlreadyTested(String message) {
      super(message);
    }
  }

  public void save(
      int plantId,
      String dataset,
      String actor,
      boolean success,
      PlantApiClient.Response response,
      long started,
      String message) {
    var previous = history(plantId, dataset, 0);
    jdbc.update(
        "INSERT INTO \"ApiTestHistory\""
            + " (\"PlantId\",\"Dataset\",\"TestedAt\",\"Actor\",\"Outcome\",\"HttpStatus\",\"ResponseMillis\",\"DurationMillis\",\"Message\")"
            + " VALUES (?,?,?,?,?,?,?,?,?)",
        plantId,
        dataset,
        OffsetDateTime.now(ZoneOffset.UTC),
        actor,
        success ? "Success" : "Failed",
        response == null ? null : response.status(),
        response == null ? null : response.elapsedMillis(),
        (System.nanoTime() - started) / 1_000_000,
        Objects.requireNonNullElse(message, "Test failed"));
    try {
      alerts.record(
          plantId,
          dataset,
          previous.isEmpty() ? null : previous.getFirst().outcome(),
          success,
          response == null ? null : response.status());
    } catch (RuntimeException e) {
      org.slf4j.LoggerFactory.getLogger(ApiTestHistoryService.class)
          .warn(
              "API result saved, but its email alert could not be queued ({})",
              e.getClass().getSimpleName());
    }
  }

  public Map<String, Entry> latest() {
    var latest = new HashMap<String, Entry>();
    query(
            "WHERE \"Id\" IN (SELECT MAX(\"Id\") FROM \"ApiTestHistory\" GROUP BY"
                + " \"PlantId\",\"Dataset\")")
        .forEach(entry -> latest.put(key(entry.plantId(), entry.dataset()), entry));
    return latest;
  }

  public List<Entry> history(int plantId, String dataset, int page) {
    return query(
        "WHERE \"PlantId\"=? AND \"Dataset\"=? ORDER BY \"Id\" DESC LIMIT 25 OFFSET ?",
        plantId,
        dataset,
        page * 25);
  }

  public long count(int plantId, String dataset) {
    return jdbc.queryForObject(
        "SELECT COUNT(*) FROM \"ApiTestHistory\" WHERE \"PlantId\"=? AND \"Dataset\"=?",
        Long.class,
        plantId,
        dataset);
  }

  public List<Entry> allHistory(int page) {
    return allHistory(page, new Filter(0, "", "", null, null));
  }

  public long countAll() {
    return countAll(new Filter(0, "", "", null, null));
  }

  public record Filter(int plantId, String dataset, String outcome, LocalDate from, LocalDate to) {}

  private record Clause(String sql, List<Object> args) {}

  private Clause filtered(Filter filter) {
    String sql =
        "WHERE \"Id\" IN (SELECT MAX(\"Id\") FROM \"ApiTestHistory\" GROUP BY"
            + " \"PlantId\",\"Dataset\")";
    var args = new ArrayList<Object>();
    if (filter.plantId() > 0) {
      sql += " AND \"PlantId\"=?";
      args.add(filter.plantId());
    }
    if (!filter.dataset().isBlank()) {
      sql += " AND \"Dataset\"=?";
      args.add(filter.dataset());
    }
    if (!filter.outcome().isBlank()) {
      sql += " AND \"Outcome\"=?";
      args.add(filter.outcome());
    }
    if (filter.from() != null) {
      sql += " AND \"TestedAt\">=?";
      args.add(filter.from().atStartOfDay(Policies.LOCAL).toOffsetDateTime());
    }
    if (filter.to() != null) {
      sql += " AND \"TestedAt\"<?";
      args.add(filter.to().plusDays(1).atStartOfDay(Policies.LOCAL).toOffsetDateTime());
    }
    return new Clause(sql, args);
  }

  public List<Entry> allHistory(int page, Filter filter) {
    var clause = filtered(filter);
    var args = new ArrayList<>(clause.args());
    args.add(Math.clamp(page, 0, 100000) * 25);
    return query(clause.sql() + " ORDER BY \"Id\" DESC LIMIT 25 OFFSET ?", args.toArray());
  }

  public long countAll(Filter filter) {
    var clause = filtered(filter);
    return jdbc.queryForObject(
        "SELECT COUNT(*) FROM \"ApiTestHistory\" " + clause.sql(),
        Long.class,
        clause.args().toArray());
  }

  public List<Entry> exportHistory(Filter filter) {
    var clause = filtered(filter);
    var entries =
        query(clause.sql() + " ORDER BY \"Id\" DESC LIMIT 10001", clause.args().toArray());
    if (entries.size() > 10000)
      throw new IllegalArgumentException(
          "Choose a narrower history filter before downloading more than 10,000 results.");
    return entries;
  }

  private List<Entry> query(String predicate, Object... args) {
    return jdbc.query(
        "SELECT * FROM \"ApiTestHistory\" " + predicate,
        (rs, row) ->
            new Entry(
                rs.getLong("Id"),
                rs.getInt("PlantId"),
                rs.getString("Dataset"),
                rs.getObject("TestedAt", OffsetDateTime.class),
                rs.getString("Actor"),
                rs.getString("Outcome"),
                (Integer) rs.getObject("HttpStatus"),
                (Long) rs.getObject("ResponseMillis"),
                rs.getLong("DurationMillis"),
                rs.getString("Message")),
        args);
  }
}
