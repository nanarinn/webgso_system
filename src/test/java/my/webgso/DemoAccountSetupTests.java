package my.webgso;

import static org.assertj.core.api.Assertions.*;

import my.webgso.Models.ApplicationUser;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.DefaultApplicationArguments;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.mock.env.MockEnvironment;
import org.springframework.security.authentication.UsernamePasswordAuthenticationToken;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.transaction.annotation.Transactional;

@SpringBootTest
@ActiveProfiles("test")
@Transactional
class DemoAccountSetupTests {
  @Autowired Store db;
  @Autowired Accounts accounts;

  @Test
  void replacementRevokesOldCredentialsAndPreservesIdentityWithoutEmailVerification() {
    var old = accounts.register("affiqah.test", "affiqahadrianna@gmail.com", "Affiqah", "OriginalPass123!");
    old.accessStatus = "Approved";
    db.update(old);
    String stamp = old.securityStamp;
    var env = new MockEnvironment()
        .withProperty("webgso.demo.replace-affiqah", "true")
        .withProperty("WEBGSO_DEMO_PASSWORD", "DemoTestingPass123!");
    var setup = new DemoAccountSetup(db, accounts, env);
    setup.run(new DefaultApplicationArguments());
    var token = accounts.authenticate(UsernamePasswordAuthenticationToken.unauthenticated(
        Accounts.DEMO_EMAIL, "DemoTestingPass123!"));
    assertThat(((Accounts.Principal) token.getPrincipal()).id()).isEqualTo(old.id);
    var updated = db.find(ApplicationUser.class, old.id).orElseThrow();
    assertThat(updated.emailConfirmed).isFalse();
    assertThat(updated.securityStamp).isNotEqualTo(stamp);
    assertThatThrownBy(() -> accounts.authenticate(UsernamePasswordAuthenticationToken.unauthenticated(
        "affiqahadrianna@gmail.com", "OriginalPass123!")));
    updated.passwordHash = new IdentityPasswordEncoder().encode("ChangedDemoPass123!");
    db.update(updated);
    setup.run(new DefaultApplicationArguments());
    assertThat(accounts.authenticate(UsernamePasswordAuthenticationToken.unauthenticated(
        Accounts.DEMO_EMAIL, "ChangedDemoPass123!")).isAuthenticated()).isTrue();
  }

  @Test
  void weakPasswordDoesNotReplaceOriginalAccount() {
    var env = new MockEnvironment().withProperty("webgso.demo.replace-affiqah", "true")
        .withProperty("WEBGSO_DEMO_PASSWORD", "weak");
    assertThatThrownBy(() -> new DemoAccountSetup(db, accounts, env)
        .run(new DefaultApplicationArguments())).isInstanceOf(IllegalArgumentException.class);
    assertThat(db.list(ApplicationUser.class, "WHERE \"NormalizedEmail\"=?", "DEMO.ADMIN@EXAMPLE.TEST")).isEmpty();
  }
}
