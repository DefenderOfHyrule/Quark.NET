package io.github.quark

import android.app.Application
import android.content.Context
import androidx.appcompat.app.AppCompatDelegate
import io.github.quark.data.ContentPathStore

class QuarkApp : Application() {
    override fun onCreate() {
        super.onCreate()
        instance = this
        ContentPathStore.init(this)
        val prefs = getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val isDark = prefs.getBoolean(KEY_DARK_MODE, true)
        AppCompatDelegate.setDefaultNightMode(
            if (isDark) AppCompatDelegate.MODE_NIGHT_YES
            else AppCompatDelegate.MODE_NIGHT_NO
        )
    }

    companion object {
        lateinit var instance: QuarkApp
            private set

        const val PREFS_NAME = "quark_ui_prefs"
        const val KEY_DARK_MODE = "dark_mode"
    }
}
