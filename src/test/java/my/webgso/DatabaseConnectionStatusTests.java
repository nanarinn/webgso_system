package my.webgso;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.*;

import org.junit.jupiter.api.Test;
import org.springframework.dao.DataAccessResourceFailureException;
import org.springframework.jdbc.core.JdbcTemplate;

class DatabaseConnectionStatusTests {
  @Test
  void checksTheDatabaseAndReportsFailureWithoutExceptionDetails() {
    var db = mock(Store.class);
    var jdbc = mock(JdbcTemplate.class);
    when(db.jdbc()).thenReturn(jdbc);
    when(jdbc.queryForObject("SELECT 1", Integer.class))
        .thenReturn(1)
        .thenThrow(new DataAccessResourceFailureException("private connection details"));
    var status = new DatabaseConnectionStatus(db);
    assertThat(status.check().connected()).isTrue();
    var failed = status.check();
    assertThat(failed.connected()).isFalse();
    assertThat(failed.checkedAt()).isNotNull();
    assertThat(failed.toString()).doesNotContain("private connection details");
    verify(jdbc, times(2)).queryForObject("SELECT 1", Integer.class);
  }
}
