package it.trsoftware.bollette;

import android.Manifest;
import android.app.AlarmManager;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.os.Build;

import org.json.JSONArray;
import org.json.JSONObject;

import java.time.LocalDate;
import java.time.format.DateTimeFormatter;
import java.time.temporal.ChronoUnit;
import java.util.Calendar;
import java.util.HashSet;
import java.util.Locale;
import java.util.Set;

/**
 * Controllo giornaliero (verso le 9) delle bollette da pagare, con una notifica per bolletta
 * e i tasti "Segna pagata" e "Apri".
 */
public class ReminderReceiver extends BroadcastReceiver {

    static final String PREFS = "promemoria";
    private static final String CHANNEL = "scadenze";
    private static final String ACTION = "it.trsoftware.bollette.CONTROLLA_SCADENZE";

    @Override
    public void onReceive(Context ctx, Intent intent) {
        if (Intent.ACTION_BOOT_COMPLETED.equals(intent.getAction())) schedule(ctx);
        final PendingResult pr = goAsync();
        new Thread(() -> {
            try {
                check(ctx.getApplicationContext());
            } finally {
                pr.finish();
            }
        }).start();
    }

    /** Ogni giorno verso le 9:00. */
    static void schedule(Context ctx) {
        AlarmManager am = (AlarmManager) ctx.getSystemService(Context.ALARM_SERVICE);
        if (am == null) return;
        Intent i = new Intent(ctx, ReminderReceiver.class).setAction(ACTION);
        PendingIntent pi = PendingIntent.getBroadcast(ctx, 1, i,
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        Calendar c = Calendar.getInstance();
        c.set(Calendar.HOUR_OF_DAY, 9);
        c.set(Calendar.MINUTE, 0);
        c.set(Calendar.SECOND, 0);
        if (c.getTimeInMillis() < System.currentTimeMillis()) c.add(Calendar.DAY_OF_MONTH, 1);
        am.setInexactRepeating(AlarmManager.RTC_WAKEUP, c.getTimeInMillis(), AlarmManager.INTERVAL_DAY, pi);
    }

    private static NotificationManager manager(Context ctx) {
        if (Build.VERSION.SDK_INT >= 33
                && ctx.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
            return null;
        NotificationManager nm = (NotificationManager) ctx.getSystemService(Context.NOTIFICATION_SERVICE);
        if (nm == null) return null;
        nm.createNotificationChannel(new NotificationChannel(CHANNEL, "Bollette da pagare",
                NotificationManager.IMPORTANCE_HIGH));
        return nm;
    }

    private static PendingIntent apri(Context ctx, String azione, String id, int code) {
        Intent open = new Intent(ctx, MainActivity.class)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP)
                .putExtra("azione", azione).putExtra("id", id);
        return PendingIntent.getActivity(ctx, code, open,
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
    }

    static int notifId(String billId) {
        return 1000 + (billId.hashCode() & 0x3fffffff) % 100000;
    }

    /** Toglie la notifica di una bolletta (quando viene segnata come pagata). */
    static void annulla(Context ctx, String billId) {
        NotificationManager nm = (NotificationManager) ctx.getSystemService(Context.NOTIFICATION_SERVICE);
        if (nm != null) nm.cancel(notifId(billId));
    }

    /** Notifica di prova. */
    static void test(Context ctx) {
        NotificationManager nm = manager(ctx);
        if (nm == null) return;
        nm.notify(101, new Notification.Builder(ctx, CHANNEL)
                .setSmallIcon(R.drawable.ic_notify)
                .setContentTitle("Notifica di prova")
                .setContentText("Gli avvisi delle bollette funzionano.")
                .setContentIntent(apri(ctx, "scadenze", "", 3))
                .setAutoCancel(true)
                .build());
    }

    static synchronized void check(Context ctx) {
        SharedPreferences p = ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        JSONObject opz;
        try {
            opz = new JSONObject(p.getString("opzioni", "{}"));
        } catch (Exception e) {
            opz = new JSONObject();
        }
        int giorniPrima = opz.optInt("giorniPrima", 5);
        boolean giornoStesso = opz.optBoolean("giornoStesso", true);
        boolean scadute = opz.optBoolean("scaduteOgniGiorno", true);
        boolean domInfo = !"no".equals(opz.optString("domiciliate", "info"));

        NotificationManager nm = manager(ctx);
        if (nm == null) return;   // senza permesso non segniamo niente: riproverà

        Set<String> notified = new HashSet<>(p.getStringSet("notificati", new HashSet<>()));
        LocalDate today = LocalDate.now();
        DateTimeFormatter it = DateTimeFormatter.ofPattern("EEEE d/MM", Locale.ITALIAN);
        int code = 10;
        try {
            JSONArray arr = new JSONArray(p.getString("items", "[]"));
            for (int k = 0; k < arr.length(); k++) {
                JSONObject o = arr.getJSONObject(k);
                String sc = o.optString("scadenza", "");
                String id = o.optString("id", "");
                if (sc.isEmpty() || id.isEmpty()) continue;
                LocalDate d;
                try {
                    d = LocalDate.parse(sc);
                } catch (Exception e) {
                    continue;
                }
                long left = ChronoUnit.DAYS.between(today, d);
                boolean dom = "dom".equals(o.optString("stato"));
                String tipo = null;
                if (dom) {
                    if (domInfo && left >= 0 && left <= 1) tipo = "addebito";
                } else if (left == 0) {
                    if (giornoStesso) tipo = "oggi";
                } else if (left > 0 && left <= giorniPrima) {
                    tipo = "prima";
                } else if (left < 0 && left >= -60 && scadute) {
                    tipo = "scaduta" + today;   // una volta al giorno
                }
                if (tipo == null) continue;
                String key = id + "|" + sc + "|" + tipo;
                if (notified.contains(key)) continue;

                String ente = o.optString("ente", "Bolletta");
                String t = o.optString("tipo", "");
                String imp = o.optString("importo", "");
                String quando = d.format(it);
                String titolo, testo;
                if (dom) {
                    titolo = "Addebito in arrivo: " + ente;
                    testo = imp + " · domiciliata, si paga da sola " + (left == 0 ? "oggi" : "domani");
                } else if (left == 0) {
                    titolo = "Scade oggi: " + ente + (t.isEmpty() ? "" : " (" + t + ")");
                    testo = imp + " da pagare oggi";
                } else if (left > 0) {
                    titolo = "Bolletta da pagare: " + ente + (t.isEmpty() ? "" : " (" + t + ")");
                    testo = imp + " · scade " + quando + " (tra " + left + (left == 1 ? " giorno)" : " giorni)");
                } else {
                    titolo = "Bolletta scaduta: " + ente + (t.isEmpty() ? "" : " (" + t + ")");
                    testo = imp + " · era da pagare entro " + quando + ". Se l'hai già pagata, tocca \"Segna pagata\".";
                }
                Notification.Builder b = new Notification.Builder(ctx, CHANNEL)
                        .setSmallIcon(R.drawable.ic_notify)
                        .setContentTitle(titolo)
                        .setContentText(testo)
                        .setStyle(new Notification.BigTextStyle().bigText(testo))
                        .setContentIntent(apri(ctx, "apri", id, code++))
                        .setAutoCancel(true);
                if (!dom) {
                    b.addAction(new Notification.Action.Builder(null, "Segna pagata", apri(ctx, "paga", id, code++)).build());
                    b.addAction(new Notification.Action.Builder(null, "Apri", apri(ctx, "apri", id, code++)).build());
                }
                nm.notify(notifId(id), b.build());
                notified.add(key);
            }
        } catch (Exception ignored) {
            return;
        }
        if (notified.size() > 600) {   // pulizia: tiene solo le chiavi degli ultimi mesi
            Set<String> keep = new HashSet<>();
            for (String k : notified) {
                String[] parts = k.split("\\|");
                try {
                    if (parts.length > 1 && LocalDate.parse(parts[1]).isAfter(today.minusDays(90))) keep.add(k);
                } catch (Exception ignored) {
                }
            }
            notified = keep;
        }
        p.edit().putStringSet("notificati", notified).apply();
    }
}
