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
  private final JdbcTemplate jdbc;

  public ApiTestHistoryService(DataSource source) {
    this.source = source;
    this.jdbc = new JdbcTemplate(source);
  }

  @PostConstruct
  void initialize() {
    new ResourceDatabasePopulator(new ClassPathResource("schema-api-test-history.sql"))
        .execute(source);
  }

  public static String key(int plantId, String dataset) {
    return plantId + ":" + Policies.canonical(dataset);
  }

  public void save(
      int plantId,
      String dataset,
      String actor,
      boolean success,
      PlantApiClient.Response response,
      long started,
      String message) {
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
    return query(
        "WHERE \"Id\" IN (SELECT MAX(\"Id\") FROM \"ApiTestHistory\" GROUP BY"
            + " \"PlantId\",\"Dataset\") ORDER BY \"Id\" DESC LIMIT 25 OFFSET ?",
        page * 25);
  }

  public long countAll() {
    return jdbc.queryForObject(
        "SELECT COUNT(*) FROM (SELECT \"PlantId\",\"Dataset\" FROM \"ApiTestHistory\" GROUP BY"
            + " \"PlantId\",\"Dataset\") latest",
        Long.class);
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
