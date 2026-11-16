package my.webgso;

import com.fasterxml.jackson.core.type.TypeReference;
import com.fasterxml.jackson.databind.ObjectMapper;
import jakarta.annotation.PostConstruct;
import java.time.*;
import java.util.*;
import javax.sql.DataSource;
import my.webgso.Models.LssPlant;
import my.webgso.PayloadTranslator.Issue;
import org.springframework.core.io.ClassPathResource;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.jdbc.datasource.init.ResourceDatabasePopulator;
import org.springframework.jdbc.support.GeneratedKeyHolder;
import org.springframework.stereotype.Service;

@Service
public class ApiAnomalyService {
  @org.springframework.beans.factory.annotation.Value("${webgso.database.shared:false}")
  private boolean sharedDatabase;
  public record Report(
      long id,
      int plantId,
      String plantCode,
      String plantName,
      String dataset,
      OffsetDateTime testedAt,
      int readings,
      int rules,
      int issueCount,
      List<Issue> issues) {}

  private final DataSource source;
  private final JdbcTemplate jdbc;
  private final ObjectMapper mapper;

  public ApiAnomalyService(DataSource source, ObjectMapper mapper) {
    this.source = source;
    this.jdbc = new JdbcTemplate(source);
    this.mapper = mapper;
  }

  @PostConstruct
  void initialize() {
    if (sharedDatabase) return;
    // Add only the report table; never initialize or reseed the existing application schema.
    new ResourceDatabasePopulator(new ClassPathResource("schema-api-validation.sql"))
        .execute(source);
  }

  public long save(LssPlant plant, String dataset, int readings, int rules, List<Issue> issues) {
    final String json;
    try {
      json = mapper.writeValueAsString(issues);
    } catch (Exception e) {
      throw new IllegalStateException("Unable to save validation findings", e);
    }
    var keys = new GeneratedKeyHolder();
    jdbc.update(
        connection -> {
          var statement =
              connection.prepareStatement(
                  "INSERT INTO \"ApiValidationReports\""
                      + " (\"PlantId\",\"PlantCode\",\"PlantName\",\"Dataset\",\"TestedAt\",\"ReadingCount\",\"RuleCount\",\"IssueCount\",\"Issues\")"
                      + " VALUES (?,?,?,?,?,?,?,?,?)",
                  new String[] {"Id"});
          statement.setInt(1, plant.id);
          statement.setString(2, plant.plantId);
          statement.setString(3, plant.name);
          statement.setString(4, dataset);
          statement.setObject(5, OffsetDateTime.now(ZoneOffset.UTC));
          statement.setInt(6, readings);
          statement.setInt(7, rules);
          statement.setInt(8, issues.size());
          statement.setString(9, json);
          return statement;
        },
        keys);
    return Objects.requireNonNull(keys.getKey()).longValue();
  }

  public List<Report> recent() {
    return query("ORDER BY \"Id\" DESC LIMIT 50");
  }

  public Optional<Report> find(long id) {
    return query("WHERE \"Id\"=?", id).stream().findFirst();
  }

  public Map<Integer, Report> latestByPlant() {
    var latest = new LinkedHashMap<Integer, Report>();
    query("WHERE \"Id\" IN (SELECT MAX(\"Id\") FROM \"ApiValidationReports\" GROUP BY \"PlantId\")")
        .forEach(report -> latest.put(report.plantId(), report));
    return latest;
  }

  private List<Report> query(String suffix, Object... args) {
    return jdbc.query(
        "SELECT * FROM \"ApiValidationReports\" " + suffix,
        (rs, row) -> {
          List<Issue> issues;
          try {
            issues = mapper.readValue(rs.getString("Issues"), new TypeReference<List<Issue>>() {});
          } catch (Exception e) {
            throw new IllegalStateException("Unable to read validation findings", e);
          }
          return new Report(
              rs.getLong("Id"),
              rs.getInt("PlantId"),
              rs.getString("PlantCode"),
              rs.getString("PlantName"),
              rs.getString("Dataset"),
              rs.getObject("TestedAt", OffsetDateTime.class),
              rs.getInt("ReadingCount"),
              rs.getInt("RuleCount"),
              rs.getInt("IssueCount"),
              issues);
        },
        args);
  }
}
