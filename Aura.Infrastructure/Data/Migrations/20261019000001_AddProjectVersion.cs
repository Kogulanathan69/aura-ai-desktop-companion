using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aura.Infrastructure.Data.Migrations;

// Source only. The operator-only command validates the disposable target before applying.
[DbContext(typeof(AuraDbContext))]
[Migration("20261019000001_AddProjectVersion")]
public sealed class AddProjectVersion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "Version", table: "Projects",
            type: "bigint", nullable: false, defaultValue: 1L);
        migrationBuilder.AddCheckConstraint("CK_Projects_Version_Positive", "Projects",
            "\"Version\" > 0");
        migrationBuilder.Sql("""
            CREATE FUNCTION "aura_increment_project_version"() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                IF TG_OP = 'INSERT' THEN
                    IF NEW."Version" IS DISTINCT FROM 1 THEN
                        RAISE EXCEPTION 'Project version must start at one';
                    END IF;
                    RETURN NEW;
                END IF;
                IF NEW."Version" IS DISTINCT FROM OLD."Version" THEN
                    RAISE EXCEPTION 'Project version is database controlled';
                END IF;
                IF OLD."Version" = 9223372036854775807 THEN
                    RAISE EXCEPTION 'Project version exhausted';
                END IF;
                NEW."Version" := OLD."Version" + 1;
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER "TR_Projects_IncrementVersion"
            BEFORE INSERT OR UPDATE ON "Projects"
            FOR EACH ROW EXECUTE FUNCTION "aura_increment_project_version"();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER \"TR_Projects_IncrementVersion\" ON \"Projects\";");
        migrationBuilder.Sql("DROP FUNCTION \"aura_increment_project_version\"();");
        migrationBuilder.DropCheckConstraint("CK_Projects_Version_Positive", "Projects");
        migrationBuilder.DropColumn(name: "Version", table: "Projects");
    }
}
