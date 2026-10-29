package my.webgso;

import java.time.*;
import org.springframework.dao.DataAccessException;
import org.springframework.stereotype.Service;

/** A real database round trip; no host names, credentials or exception details are exposed. */
@Service
public class DatabaseConnectionStatus {
  public record Snapshot(boolean connected, OffsetDateTime checkedAt) {}

  private final Store db;

  public DatabaseConnectionStatus(Store db) {
    this.db = db;
  }

  public Snapshot check() {
    boolean connected;
    try {
      connected = Integer.valueOf(1).equals(db.jdbc().queryForObject("SELECT 1", Integer.class));
    } catch (DataAccessException e) {
      connected = false;
    }
    return new Snapshot(connected, OffsetDateTime.now(ZoneOffset.UTC));
  }
}
