package io.github.quark.service

import kotlin.random.Random

object ConsoleId {

    private val adjectives = listOf(
        "Red", "Blue", "Gold", "Swift", "Brave", "Calm", "Dark", "Jade",
        "Iron", "Keen", "Lime", "Neon", "Opal", "Pink", "Rosy", "Sage",
        "Teal", "Volt", "Warm", "Zest", "Bold", "Cool", "Dusk", "Epic",
        "Fast", "Glow", "Hazy", "Icy", "Just", "Kiwi",
    )

    private val nouns = listOf(
        "Switch", "Nova", "Pixel", "Spark", "Drift", "Flame", "Ghost",
        "Haven", "Ivory", "Jewel", "Karma", "Lunar", "Mango", "Nexus",
        "Orbit", "Prism", "Quest", "Radar", "Solar", "Titan", "Unity",
        "Vapor", "Wave", "Xenon", "Yield", "Zenith", "Apex", "Blaze",
        "Comet", "Delta",
    )

    fun generate(): String {
        val adjective = adjectives[Random.nextInt(adjectives.size)]
        val noun = nouns[Random.nextInt(nouns.size)]
        return "$adjective-$noun"
    }
}
