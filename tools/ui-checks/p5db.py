"""Scratch-database helper for the Phase 5 UI checks (the app must be closed for writes).

python p5db.py <db> set key=value [key=value ...]
python p5db.py <db> unset key [key ...]
python p5db.py <db> stopall
python p5db.py <db> lastfired <title>
"""
import sqlite3
import sys
import time

db, cmd, *args = sys.argv[1:]
con = sqlite3.connect(db)
now_ms = int(time.time() * 1000)

if cmd == 'set':
    for kv in args:
        k, v = kv.split('=', 1)
        v = v.replace('{now+1h}', str(now_ms + 3600_000))
        con.execute('INSERT INTO settings(key, value) VALUES(?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value', (k, v))
elif cmd == 'unset':
    for k in args:
        con.execute('DELETE FROM settings WHERE key = ?', (k,))
elif cmd == 'stopall':
    con.execute('UPDATE entries SET stop_utc = ? WHERE stop_utc IS NULL AND start_utc < ?', (now_ms, now_ms - 1000))
elif cmd == 'lastfired':
    row = con.execute('SELECT last_fired_utc FROM reminders WHERE title = ?', (args[0],)).fetchone()
    print('none' if row is None or row[0] is None else row[0])
con.commit()
con.close()
