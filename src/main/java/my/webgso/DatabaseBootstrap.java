package my.webgso;

import java.math.BigDecimal;
import java.time.*;
import java.util.*;
import javax.sql.DataSource;
import my.webgso.Models.*;
import org.springframework.boot.ApplicationArguments;
import org.springframework.boot.ApplicationRunner;
import org.springframework.core.env.Environment;
import org.springframework.core.io.ClassPathResource;
import org.springframework.jdbc.datasource.init.ResourceDatabasePopulator;
import org.springframework.stereotype.Component;

@Component
@org.springframework.core.annotation.Order(0)
public class DatabaseBootstrap implements ApplicationRunner {
  private final Store db;
  private final Accounts accounts;
  private final Environment env;
  private final DataSource dataSource;

  public DatabaseBootstrap(Store db, Accounts accounts, Environment env, DataSource dataSource) {
    this.db = db;
    this.accounts = accounts;
    this.env = env;
    this.dataSource = dataSource;
  }

  @Override
  public void run(ApplicationArguments args) {
    boolean demo = Arrays.asList(env.getActiveProfiles()).contains("demo");
    if (env.getProperty("webgso.schema.initialize", Boolean.class, false))
      new ResourceDatabasePopulator(new ClassPathResource("schema-java.sql")).execute(dataSource);
    if (env.getProperty("webgso.schema.initialize", Boolean.class, false))
      for (var definition : RuleCatalog.DEFINITIONS) {
        if (!db.list(ValidationRule.class, "WHERE \"Code\"=?", definition.code()).isEmpty())
          continue;
        var rule = new ValidationRule();
        rule.code = definition.code();
        rule.dataType = definition.apiType();
        rule.name = definition.name();
        rule.description = definition.description();
        rule.condition = definition.condition();
        rule.severity = definition.severity();
        rule.fieldName = "*";
        db.insert(rule);
      }
    String username = env.getProperty("webgso.bootstrap.username", "");
    if (demo && username.isBlank()) username = "demo.admin";
    if (!username.isBlank()) {
      String password = env.getProperty("webgso.bootstrap.password", demo ? "DemoAdmin123!" : "");
      String email =
          env.getProperty("webgso.bootstrap.email", demo ? "demo.admin@example.test" : "");
      if (db.list(
              ApplicationUser.class,
              "WHERE \"NormalizedUserName\"=?",
              username.toUpperCase(Locale.ROOT))
          .isEmpty()) {
        var admin = accounts.register(username, email, "GSO Admin", password);
        admin.accessStatus = "Approved";
        db.update(admin);
        accounts.setRole(admin.id, Accounts.ADMIN);
      }
    }
    if (!demo || db.count(LssPlant.class) > 0) return;
    for (int i = 1; i <= 4; i++) {
      var plant = new LssPlant();
      plant.plantId = String.format("LSS-%03d", i);
      plant.name =
          List.of("Solar Aman", "Suria Hijau", "Cahaya Selatan", "Tenaga Utara").get(i - 1);
      plant.state = List.of("Selangor", "Perak", "Johor", "Kedah").get(i - 1);
      plant.capacityMw = BigDecimal.valueOf(30 + i * 5L);
      db.insert(plant);
      var job = new CollectionJobEntity();
      job.collectionId = "DEMO-" + i;
      job.plantId = plant.id;
      job.dataType = "Rolling 24 Hour";
      job.mode = "Demo";
      job.status = "Completed";
      job.startedAt = OffsetDateTime.now(ZoneOffset.UTC);
      job.completedAt = job.startedAt;
      job.submissionDate = LocalDate.now(Policies.LOCAL);
      job.submissionDateTime = job.submissionDate.atStartOfDay();
      job.requestedBy = "Demo seed";
      db.insert(job);
      for (int p = 0; p < 96; p++) {
        double hour = p / 4.0, solar = Math.max(0, Math.sin(Math.PI * (hour - 6) / 12));
        var r = new SolarApiReading();
        r.plantId = plant.id;
        r.collectionJobId = job.id;
        r.dataType = "Rolling 24 Hour";
        r.submissionDateTime = job.submissionDateTime;
        r.intervalStart = job.submissionDateTime.plusMinutes(p * 15L);
        r.intervalEnd = r.intervalStart.plusMinutes(15);
        r.intervalMinutes = 15;
        r.parameter = "Value";
        r.unit = "MW";
        r.value =
            BigDecimal.valueOf(solar * plant.capacityMw.doubleValue() * .85)
                .setScale(6, java.math.RoundingMode.HALF_EVEN);
        r.isSynthetic = true;
        r.syntheticReason = "Demo data";
        db.insert(r);
        for (var param : Policies.MMF_UNITS.entrySet()) {
          var mmf = new SolarApiReading();
          mmf.plantId = plant.id;
          mmf.collectionJobId = job.id;
          mmf.dataType = "MMF";
          mmf.submissionDateTime = r.submissionDateTime;
          mmf.intervalStart = r.intervalStart;
          mmf.intervalEnd = r.intervalEnd;
          mmf.intervalMinutes = 15;
          mmf.parameter = param.getKey();
          mmf.unit = param.getValue();
          mmf.value =
              BigDecimal.valueOf(
                      param.getKey().contains("Temp")
                          ? 24 + solar * 10
                          : param.getKey().contains("Irradiance") ? solar * 950 : solar * 10 + 2)
                  .setScale(6, java.math.RoundingMode.HALF_EVEN);
          mmf.isSynthetic = true;
          mmf.syntheticReason = "Demo data";
          db.insert(mmf);
        }
      }
    }
  }
}
