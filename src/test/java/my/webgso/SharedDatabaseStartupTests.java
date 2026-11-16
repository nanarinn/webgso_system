package my.webgso;

import static org.mockito.Mockito.*;

import com.fasterxml.jackson.databind.ObjectMapper;
import javax.sql.DataSource;
import org.junit.jupiter.api.Test;
import org.springframework.boot.DefaultApplicationArguments;
import org.springframework.mock.env.MockEnvironment;
import org.springframework.test.util.ReflectionTestUtils;

class SharedDatabaseStartupTests {
  @Test
  void sharedStartupDoesNotCreateSchemaRulesOrBootstrapAccounts() {
    var source = mock(DataSource.class);
    var db = mock(Store.class);
    var accounts = mock(Accounts.class);
    var env = new MockEnvironment()
        .withProperty("webgso.database.shared", "true")
        .withProperty("webgso.schema.initialize", "true")
        .withProperty("webgso.bootstrap.username", "must-not-be-created")
        .withProperty("webgso.demo.replace-affiqah", "true");
    var args = new DefaultApplicationArguments();
    new DatabaseBootstrap(db, accounts, env, source).run(args);
    new DemoAccountSetup(db, accounts, env).run(args);
    verifyNoInteractions(source, db, accounts);
  }

  @Test
  void sharedStartupDoesNotRunAccountDdl() {
    var source = mock(DataSource.class);
    var schema = new AccountToolsSchema(source);
    ReflectionTestUtils.setField(schema, "sharedDatabase", true);
    schema.run(new DefaultApplicationArguments());
    verifyNoInteractions(source);
  }

  @Test
  void sharedStartupDoesNotCreateHistoryTablesOrDeleteExistingClaims() {
    var source = mock(DataSource.class);
    var history = new ApiTestHistoryService(source, mock(ApiEmailAlerts.class));
    ReflectionTestUtils.setField(history, "sharedDatabase", true);
    history.initialize();
    verifyNoInteractions(source);
  }

  @Test
  void sharedStartupDoesNotCreateValidationTables() {
    var source = mock(DataSource.class);
    var anomalies = new ApiAnomalyService(source, new ObjectMapper());
    ReflectionTestUtils.setField(anomalies, "sharedDatabase", true);
    anomalies.initialize();
    verifyNoInteractions(source);
  }
}
