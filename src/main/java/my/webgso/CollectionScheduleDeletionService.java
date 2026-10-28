package my.webgso;

import my.webgso.Models.CollectionSchedule;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
public class CollectionScheduleDeletionService {
  private final Store db;
  private final Accounts accounts;

  public CollectionScheduleDeletionService(Store db, Accounts accounts) {
    this.db = db;
    this.accounts = accounts;
  }

  @Transactional
  public String delete(int id, String actor) {
    if (db.jdbc()
        .queryForList("SELECT \"Id\" FROM \"CollectionSchedules\" WHERE \"Id\"=? FOR UPDATE", id)
        .isEmpty()) throw new IllegalArgumentException("Collection schedule not found.");
    var schedule = db.find(CollectionSchedule.class, id).orElseThrow();
    long running =
        db.jdbc()
            .queryForObject(
                "SELECT COUNT(*) FROM \"CollectionJobs\" WHERE \"CollectionScheduleId\"=? AND"
                    + " \"Status\" IN ('Running','Processing','Queued')",
                Long.class,
                id);
    if (running > 0)
      throw new IllegalArgumentException(
          "This schedule has a collection in progress. Wait until it finishes before deleting the"
              + " schedule.");
    db.jdbc()
        .update(
            "UPDATE \"CollectionJobs\" SET \"CollectionScheduleId\"=NULL WHERE"
                + " \"CollectionScheduleId\"=?",
            id);
    db.jdbc().update("DELETE FROM \"CollectionSchedules\" WHERE \"Id\"=?", id);
    accounts.audit(
        actor,
        "Delete collection schedule",
        "CollectionSchedule",
        schedule.scheduleCode,
        "Success",
        schedule.name);
    return schedule.name;
  }
}
