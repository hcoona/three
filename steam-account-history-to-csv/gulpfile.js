"use strict";

var gulp = require("gulp");
var ts = require("gulp-typescript");
var tslint = require("gulp-tslint");
var sourcemaps = require("gulp-sourcemaps");
var template = require("gulp-template");
var rimraf = require("rimraf");

let destinationFolderPath = "dist";

// Clean dist folder in synchrony way
rimraf.sync(destinationFolderPath);

gulp.task("default", ["build:typescript", "build:manifest", "copy:icon"]);

gulp.task("build:typescript", function () {
  var tsProject = ts.createProject('tsconfig.json');
  var tsResult = tsProject.src()
    .pipe(tslint({ formatter: "verbose" }))
    .pipe(sourcemaps.init())
    .pipe(tsProject());
  return tsResult.js
    .pipe(sourcemaps.write("."))
    .pipe(gulp.dest(destinationFolderPath));
});

gulp.task("build:manifest", function () {
  var pkg = require("./package.json");
  return gulp.src("assets/manifest.json")
    .pipe(template(pkg))
    .pipe(gulp.dest(destinationFolderPath));
})

gulp.task("copy:icon", function() {
  return gulp.src("assets/*.png").pipe(gulp.dest(destinationFolderPath));
})
