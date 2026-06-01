using ApartmentTriage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApartmentTriage.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(ApartmentTriageDbContext))]
    [Migration("20260601120000_202606011500_NormalizeResidentPhoneNumbers")]
    /// <summary>
    /// Canonicalizes Turkish phone numbers to E.164 (+90…) and merges duplicate residents
    /// created when UI stored +905… but WhatsApp webhook stored 905… without '+'.
    /// Review NOTICE output after apply on production (Neon) for merged row ids.
    /// </summary>
    public partial class _202606011500_NormalizeResidentPhoneNumbers : Migration
    {
        private const string CanonicalWhatsAppSql = """
            CASE
                WHEN whats_app_number IS NULL OR whats_app_number LIKE '%*%' OR whats_app_number = '[redacted]' THEN NULL
                WHEN whats_app_number ~ '^\+90[0-9]{10}$' THEN whats_app_number
                WHEN regexp_replace(whats_app_number, '[\s\-().]', '', 'g') ~ '^90[0-9]{10}$'
                    THEN '+' || regexp_replace(whats_app_number, '[\s\-().]', '', 'g')
                WHEN regexp_replace(whats_app_number, '[\s\-().]', '', 'g') ~ '^5[0-9]{9}$'
                    THEN '+90' || regexp_replace(whats_app_number, '[\s\-().]', '', 'g')
                WHEN regexp_replace(whats_app_number, '[\s\-().]', '', 'g') ~ '^05[0-9]{9}$'
                    THEN '+90' || substring(regexp_replace(whats_app_number, '[\s\-().]', '', 'g') from 2)
                ELSE whats_app_number
            END
            """;

        private const string CanonicalContactPhoneSql = """
            CASE
                WHEN contact_phone IS NULL OR contact_phone LIKE '%*%' THEN NULL
                WHEN contact_phone ~ '^\+90[0-9]{10}$' THEN contact_phone
                WHEN regexp_replace(contact_phone, '[\s\-().]', '', 'g') ~ '^90[0-9]{10}$'
                    THEN '+' || regexp_replace(contact_phone, '[\s\-().]', '', 'g')
                WHEN regexp_replace(contact_phone, '[\s\-().]', '', 'g') ~ '^5[0-9]{9}$'
                    THEN '+90' || regexp_replace(contact_phone, '[\s\-().]', '', 'g')
                WHEN regexp_replace(contact_phone, '[\s\-().]', '', 'g') ~ '^05[0-9]{9}$'
                    THEN '+90' || substring(regexp_replace(contact_phone, '[\s\-().]', '', 'g') from 2)
                ELSE contact_phone
            END
            """;

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Merge duplicate whats_app_number by canonical form (before UPDATE) ──
            migrationBuilder.Sql($"""
                DO $$
                DECLARE
                    r RECORD;
                    keep_id uuid;
                    drop_id uuid;
                BEGIN
                    FOR r IN
                        SELECT canon AS num,
                               (array_agg(id ORDER BY created_at))[1] AS keeper,
                               (array_agg(id ORDER BY created_at))[2:] AS losers
                        FROM (
                            SELECT id, created_at, {CanonicalWhatsAppSql} AS canon
                            FROM residents
                        ) normalized
                        WHERE canon IS NOT NULL
                        GROUP BY canon
                        HAVING count(*) > 1
                    LOOP
                        keep_id := r.keeper;
                        FOREACH drop_id IN ARRAY r.losers
                        LOOP
                            UPDATE messages SET resident_id = keep_id WHERE resident_id = drop_id;
                            UPDATE tickets SET resident_id = keep_id WHERE resident_id = drop_id;
                            UPDATE residents SET whats_app_number = NULL WHERE id = drop_id;
                            RAISE NOTICE 'Merged duplicate WhatsApp %: dropped resident % into %',
                                r.num, drop_id, keep_id;
                        END LOOP;
                    END LOOP;
                END $$;
                """);

            // ── 2. Merge duplicate contact_phone by canonical form (before UPDATE) ──
            migrationBuilder.Sql($"""
                DO $$
                DECLARE
                    r RECORD;
                    keep_id uuid;
                    drop_id uuid;
                BEGIN
                    FOR r IN
                        SELECT canon AS num,
                               (array_agg(id ORDER BY created_at))[1] AS keeper,
                               (array_agg(id ORDER BY created_at))[2:] AS losers
                        FROM (
                            SELECT id, created_at, {CanonicalContactPhoneSql} AS canon
                            FROM residents
                        ) normalized
                        WHERE canon IS NOT NULL
                        GROUP BY canon
                        HAVING count(*) > 1
                    LOOP
                        keep_id := r.keeper;
                        FOREACH drop_id IN ARRAY r.losers
                        LOOP
                            IF drop_id <> keep_id THEN
                                UPDATE residents SET contact_phone = NULL WHERE id = drop_id;
                                RAISE NOTICE 'Cleared duplicate contact_phone % on resident % (keeper %)',
                                    r.num, drop_id, keep_id;
                            END IF;
                        END LOOP;
                    END LOOP;
                END $$;
                """);

            // ── 3. Normalize whats_app_number and contact_phone (skip masked/redacted) ──
            migrationBuilder.Sql($"""
                UPDATE residents
                SET whats_app_number = {CanonicalWhatsAppSql}
                WHERE whats_app_number IS NOT NULL;

                UPDATE residents
                SET contact_phone = {CanonicalContactPhoneSql}
                WHERE contact_phone IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_residents_contact_phone",
                table: "residents",
                column: "contact_phone",
                unique: true,
                filter: "contact_phone IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_residents_contact_phone",
                table: "residents");
        }
    }
}
