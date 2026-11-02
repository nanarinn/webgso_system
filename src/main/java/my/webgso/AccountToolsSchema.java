package my.webgso;

import javax.sql.DataSource;
import org.springframework.boot.ApplicationArguments;
import org.springframework.boot.ApplicationRunner;
import org.springframework.core.annotation.Order;
import org.springframework.core.io.ClassPathResource;
import org.springframework.jdbc.datasource.init.ResourceDatabasePopulator;
import org.springframework.stereotype.Component;

@Component
@Order(100)
public class AccountToolsSchema implements ApplicationRunner {
  private final DataSource source;

  public AccountToolsSchema(DataSource source) {
    this.source = source;
  }

  @Override
  public void run(ApplicationArguments arguments) {
    new ResourceDatabasePopulator(new ClassPathResource("schema-account-tools.sql"))
        .execute(source);
  }
}
