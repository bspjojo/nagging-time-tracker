"""Scratch-database helper for the Phase 6 and 7 UI checks (writes only while the app is closed).

python p6db.py <db> seed <start_min_ago> <heartbeat_min_ago>   # one running Acme entry + heartbeat
python p6db.py <db> entries                                    # id|start|stop|continued_from per row
python p6db.py <db> get <key>
python p6db.py <db> unset <key> [key ...]
python p6db.py <db> heartbeat                                  # the stored heartbeat (ms)
"""
import sqlite3
import sys
import time

db, cmd, *args = sys.argv[1:]
con = sqlite3.connect(db)
now_ms = int(time.time() * 1000)

if cmd == 'seed':
    start = now_ms - int(float(args[0]) * 60_000)
    beat = now_ms - int(float(args[1]) * 60_000)
    con.execute('DELETE FROM entries')
    row = con.execute("SELECT id FROM customers WHERE name = 'Acme'").fetchone()
    cid = row[0] if row else con.execute(
        "INSERT INTO customers(name, archived, created_utc) VALUES('Acme', 0, ?)", (now_ms,)).lastrowid
    con.execute(
        "INSERT INTO entries(customer_id, task_type_id, start_utc, stop_utc, notes, created_utc, updated_utc) "
        "VALUES(?, 3, ?, NULL, '', ?, ?)", (cid, start, start, start))
    con.execute(
        'INSERT INTO settings(key, value) VALUES(?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value',
        ('heartbeat_utc', str(beat)))
    print(beat)
elif cmd == 'entries':
    for r in con.execute('SELECT id, start_utc, stop_utc, continued_from_id FROM entries ORDER BY id'):
        print('|'.join('' if v is None else str(v) for v in r))
elif cmd == 'unset':
    for k in args:
        con.execute('DELETE FROM settings WHERE key = ?', (k,))
elif cmd == 'get':
    row = con.execute('SELECT value FROM settings WHERE key = ?', (args[0],)).fetchone()
    print('none' if row is None else row[0])
con.commit()
con.close()
