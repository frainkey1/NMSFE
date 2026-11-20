# Platform support — Preview5

Updated2026-11-08.

Player field rows use consistent40DIP minimum heights,32DIP inputs and420DIP cards; content reflows and scrolls without unequal column widths.

Supported through local files, not cloud-account login:
- Steam/GOG: existing direct save and slot utilities.
- PS4: decrypted/extracted streaming savedataNN.hg and NOMANSKY header direct editing; extracted streaming slot utilities/transfer. Retail encryptedPS5 and monolithic memory.dat remain unsupported.
- Switch: extracted savedataNN.hg + manifest direct editing/slot utilities/transfers.
- Xbox/GamePass: existing WGS/XGS container slots; choose folder and stable slot identifier throughOpen. Metadata and indexes retained; XGS sibling WGS mirror included in backup, conflict detection,verification and rollback. Transfer requires an existing destination slot. Console account rewards are read-only/unavailable; no new Xbox slot creation or ownership migration.

Verification: temporary synthetic fixtures test each format, metadata2001/2002/2003/2004 sizes, UTF8NOMANSKYheader, copies/moves/swaps/deletes, bidirectional transfer, conflicts, partial mutations, locked mirror rollback and restore-failure best effort. No physical-console import or in-game save/reload was performed. Real Steam save8.hg only read for UI/copy tests; sourcehash unchanged.

Research:
https://www.nomanssky.com/buy-now/ lists Steam,GOG,Xbox,PlayStation,Switch,Mac,iOS; no EpicGamesStore or Android listing was found. This is not proof a future edition cannot exist. No unverified adapters or options added.
https://www.nomanssky.com/2026/10/no-mans-sky-ios/ confirms iPhone/iPad release and feature differences. Accessible exported save format/metadata has not been established with a representative fixture. User said defer if not similar: mobile is deferred.

Latest editor: Build/Preview5/NMSE.Next.exe. Older previews retained as baselines.
