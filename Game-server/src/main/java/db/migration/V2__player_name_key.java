package db.migration;

import com.golfsim.server.game.Names;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.Statement;
import java.util.HashSet;
import java.util.Set;
import org.flywaydb.core.api.migration.BaseJavaMigration;
import org.flywaydb.core.api.migration.Context;

/**
 * Replaces the {@code lower(name)} unique index with a stored {@code name_key} computed by {@link Names#key}, so the
 * database and the Java lookups agree on which names are the same player (GS-6). Existing players whose keys
 * collide keep distinct keys by appending {@code #<id>} to the newer one.
 */
public class V2__player_name_key extends BaseJavaMigration {

    @Override
    public void migrate(Context context) throws Exception {
        Connection db = context.getConnection();
        try (Statement ddl = db.createStatement()) {
            ddl.execute("alter table players add column name_key text");
        }
        Set<String> taken = new HashSet<>();
        try (Statement select = db.createStatement();
                ResultSet rows = select.executeQuery("select id, name from players order by id");
                PreparedStatement update = db.prepareStatement("update players set name_key = ? where id = ?")) {
            while (rows.next()) {
                long id = rows.getLong(1);
                String key = Names.key(rows.getString(2));
                if (!taken.add(key)) {
                    key = key + "#" + id;
                    taken.add(key);
                }
                update.setString(1, key);
                update.setLong(2, id);
                update.executeUpdate();
            }
        }
        try (Statement ddl = db.createStatement()) {
            ddl.execute("alter table players alter column name_key set not null");
            ddl.execute("drop index players_name_ci_uk");
            ddl.execute("create unique index players_name_key_uk on players (name_key)");
        }
    }
}
