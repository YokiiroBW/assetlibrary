import org.jetbrains.kotlin.gradle.dsl.JvmTarget

plugins {
    kotlin("jvm") version "2.3.20"
}

group = "app.assetlibrary"
version = "0.1.0"

kotlin {
    explicitApi()
    jvmToolchain(21)
    compilerOptions {
        allWarningsAsErrors.set(true)
        jvmTarget.set(JvmTarget.JVM_21)
    }
}

dependencies {
    implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.11.0")
}

dependencyLocking {
    lockAllConfigurations()
}

val sdkTestSourceSet = sourceSets.create("sdkTest") {
    kotlin.srcDir("src/sdkTest/kotlin")
    compileClasspath += sourceSets.main.get().output + configurations.runtimeClasspath.get()
    runtimeClasspath += output + compileClasspath
}

val sdkTest by tasks.registering(JavaExec::class) {
    group = LifecycleBasePlugin.VERIFICATION_GROUP
    description = "Runs the dependency-minimal generated SDK verification suite."
    dependsOn(tasks.named(sdkTestSourceSet.classesTaskName))
    classpath = sdkTestSourceSet.runtimeClasspath
    mainClass.set("app.assetlibrary.assetlink.AssetLinkGeneratedTestKt")
}

tasks.check {
    dependsOn(sdkTest)
}
