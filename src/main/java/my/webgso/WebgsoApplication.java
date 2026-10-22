package my.webgso;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.scheduling.annotation.EnableScheduling;

@SpringBootApplication
@EnableScheduling
public class WebgsoApplication {
  public static void main(String[] args) {
    SpringApplication.run(WebgsoApplication.class, args);
  }
}
