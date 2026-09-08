import org.jetbrains.kotlin.gradle.dsl.JvmTarget

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "app.assetlibrary.android"
    compileSdk = 37
    buildToolsVersion = "36.0.0"
    defaultConfig {
        applicationId = "app.assetlibrary.android"
        minSdk = 30
        targetSdk = 36
        versionCode = 1
        versionName = "0.3.0-readonly.1"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }
    buildFeatures { compose = true }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    sourceSets {
        getByName("main") {
            kotlin.directories.add("../../../packages/sdk/assetlink/kotlin/src/main/kotlin")
            assets.directories.add("../../../packages/ui")
        }
    }
    lint {
        abortOnError = true
        warningsAsErrors = true
        checkDependencies = true
        disable += "GradleDependency" // Frozen stable patches are reviewed through dependency verification.
        disable += "OldTargetApi" // This trial targets verified Android 16 behavior; API 37 target migration is separate.
    }
    testOptions { unitTests.isReturnDefaultValues = false }
    packaging { resources.excludes += "/META-INF/{AL2.0,LGPL2.1}" }
}
kotlin { compilerOptions { jvmTarget.set(JvmTarget.JVM_17); allWarningsAsErrors.set(true) } }
dependencyLocking {
    lockAllConfigurations()
    lockMode.set(LockMode.STRICT)
}
tasks.withType<Test>().configureEach {
    // The JDK URLConnection test implementation restricts Origin; Android's implementation does not.
    systemProperty("sun.net.http.allowRestrictedHeaders", "true")
}

dependencies {
    val compose = platform("androidx.compose:compose-bom:2026.08.00")
    implementation(compose)
    androidTestImplementation(compose)
    implementation("androidx.activity:activity-compose:1.13.0")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.11.0")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.11.0")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.ui:ui")
    implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.11.0")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.10.2")
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.jetbrains.kotlinx:kotlinx-coroutines-test:1.10.2")
    androidTestImplementation("androidx.test:runner:1.7.0")
    androidTestImplementation("androidx.test.ext:junit:1.3.0")
    androidTestImplementation("androidx.compose.ui:ui-test-junit4")
    debugImplementation("androidx.compose.ui:ui-test-manifest")
}

tasks.register("dependencyInventory") {
    val destination = layout.buildDirectory.file("reports/dependency-inventory.tsv")
    val runtimeDestination = layout.buildDirectory.file("reports/runtime-dependency-inventory.tsv")
    outputs.file(destination)
    outputs.file(runtimeDestination)
    outputs.upToDateWhen { false }
    doLast {
        val rows = configurations.filter { it.isCanBeResolved }.flatMap { configuration ->
            configuration.incoming.resolutionResult.allComponents.filter {
                it.id is org.gradle.api.artifacts.component.ModuleComponentIdentifier
            }.mapNotNull { it.moduleVersion }
                .map { "${it.group}\t${it.name}\t${it.version}" }
        }.distinct().sorted()
        destination.get().asFile.apply { parentFile.mkdirs(); writeText(rows.joinToString("\n") + "\n") }
        val runtimeRows = listOf("debugRuntimeClasspath", "releaseRuntimeClasspath").flatMap { name ->
            configurations.getByName(name).incoming.resolutionResult.allComponents.filter {
                it.id is org.gradle.api.artifacts.component.ModuleComponentIdentifier
            }.mapNotNull { it.moduleVersion }.map { "${it.group}\t${it.name}\t${it.version}" }
        }.distinct().sorted()
        runtimeDestination.get().asFile.writeText(runtimeRows.joinToString("\n") + "\n")
    }
}
