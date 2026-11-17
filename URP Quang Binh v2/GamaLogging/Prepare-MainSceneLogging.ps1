param([switch]$Install)
$ErrorActionPreference = 'Stop'
$encoding = New-Object System.Text.UTF8Encoding($false)
function Replace-One([string]$text, [string]$old, [string]$new) {
    $old = $old.Replace("`r`n", "`n")
    $new = $new.Replace("`r`n", "`n")
    $count = ([regex]::Matches($text, [regex]::Escape($old))).Count
    if ($count -ne 1) { throw "Expected exactly one match, got ${count}: $old" }
    return $text.Replace($old, $new)
}
if ($Install) {
    $manifest = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'prepared-files.json') | ConvertFrom-Json
    $allowed = @()
    foreach ($root in @(
        'C:/Users/GIGABYTE/Gama_Workspace/Gama/models/version 2',
        'C:/Users/GIGABYTE/UnityProjects/simple.webplatform/learning-packages/Quanh-Binh/models/version 2'
    )) {
        foreach ($name in 'Flooding Model.gaml','Flooding VR.gaml') {
            $allowed += [IO.Path]::GetFullPath((Join-Path $root $name))
        }
    }
    if ($manifest.Count -ne 4) { throw 'Expected four named GAMA files' }
    foreach ($entry in $manifest) {
        if ($allowed -notcontains [IO.Path]::GetFullPath($entry.destination)) { throw 'Unexpected destination' }
        if ((Get-FileHash -LiteralPath $entry.destination -Algorithm SHA256).Hash -ne $entry.original_sha256) {
            throw "File changed since preparation: $($entry.destination)"
        }
        $prepared = Join-Path $PSScriptRoot $entry.prepared
        if ((Get-FileHash -LiteralPath $prepared -Algorithm SHA256).Hash -ne $entry.prepared_sha256) {
            throw "Prepared file changed: $prepared"
        }
    }
    foreach ($entry in $manifest) {
        [IO.File]::WriteAllBytes($entry.destination, [IO.File]::ReadAllBytes((Join-Path $PSScriptRoot $entry.prepared)))
        Write-Output "Updated: $($entry.destination)"
    }
    exit
}
$locations = @(
    @{ name='Workspace'; folder='C:/Users/GIGABYTE/Gama_Workspace/Gama/models/version 2' },
    @{ name='WebPlatform'; folder='C:/Users/GIGABYTE/UnityProjects/simple.webplatform/learning-packages/Quanh-Binh/models/version 2' }
)
$manifest = @()
foreach ($location in $locations) {
    $out = Join-Path $PSScriptRoot $location.name
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    foreach ($name in @('Flooding Model.gaml', 'Flooding VR.gaml')) {
        $path = Join-Path $location.folder $name
        $text = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
        if ($text.Contains('main_scene_log')) { throw "Logging already present in $path; inspect instead of replacing it." }
        if ($name -eq 'Flooding Model.gaml') {
            $text = Replace-One $text "`tfloat cell_percentage;`n`tinit {`n`t`tlength <- shape.perimeter;" @'
	float cell_percentage;
	// Preserve the original line before buffering its display geometry.
	point main_scene_log_start;
	point main_scene_log_end;
	init {
		main_scene_log_start <- first(shape.points);
		main_scene_log_end <- last(shape.points);
		length <- shape.perimeter;
'@
        } else {
            $text = Replace-One $text "`taction exit_flooding {`n`t`tdo exit_flooding_base;" @'
	action exit_flooding {
		// Capture BEFORE exit_flooding_base increments/reset the round.
		ask unity_linker { do send_main_scene_snapshot("simulation_end"); }
		do exit_flooding_base;
'@
            $old = @'
			do send_message players: unity_player as list mes: ["end_diking"::""];
'@
            $new = @'
			do send_main_scene_snapshot("build_end");
			do send_message players: unity_player as list mes: ["end_diking"::""];
'@
            $text = Replace-One $text $old $new
            $old = @'
		loop each_player over: unity_player {
			ask unity_linker{ do get_remaining_resources(each_player.name); }
		}
'@
            $new = $old + "`n`t`task unity_linker { do send_message players: unity_player as list mes: [`"main_scene_log`" :: main_scene_metrics(`"round_start`")]; }"
            $text = Replace-One $text $old $new
            $old = @'
	reflex send_agents when: not empty(unity_player) {
'@
            $new = @'
	reflex send_agents when: not empty(unity_player) {
		do queue_main_scene_geometries;
	}

	action queue_main_scene_geometries {
'@
            $text = Replace-One $text $old $new
            $old = @'
	action add_to_send_world(map map_to_send) {
'@
            $new = @'
	// Authoritative main-scene logging, separate from tutorial measurements.
	string main_scene_stage <- "world_state";
	int main_scene_event_counter <- 0;

	map main_scene_barrier_record(dyke barrier) {
		point a <- barrier.main_scene_log_start;
		point b <- barrier.main_scene_log_end;
		return ["gama_name" :: barrier.name,
			"structure_type" :: (barrier.is_dam ? "dam" : "dyke"),
			"player_id" :: (dykes_built_by_players[barrier.name] = nil ? "Unknown" : dykes_built_by_players[barrier.name]),
			"length_m" :: barrier.length,
			"remaining_length_m" :: (barrier.length * barrier.cell_percentage),
			"start_gama" :: [a.x, a.y, a.z], "end_gama" :: [b.x, b.y, b.z],
			"drowned" :: barrier.drowned];
	}

	map main_scene_metrics(string event_type) {
		main_scene_event_counter <- main_scene_event_counter + 1;
		return ["schema_version" :: 1,
			"event_id" :: string(main_scene_event_counter),
			"event_type" :: event_type, "round" :: world.current_round,
			"state" :: world.state, "current_step" :: world.current_step,
			"num_step" :: world.num_step, "gama_machine_time_ms" :: gama.machine_time,
			"dyke_length" :: world.dyke_length, "dam_length" :: world.dam_length,
			"evacuated" :: world.evacuated, "casualties" :: world.casualties,
			"score" :: world.score,
			"barriers" :: (dyke collect main_scene_barrier_record(each))];
	}

	action send_main_scene_snapshot(string stage) {
		if (not empty(unity_player)) {
			main_scene_stage <- stage;
			do queue_main_scene_geometries;
			do send_world;
			main_scene_stage <- "world_state";
		}
	}

	action add_to_send_world(map map_to_send) {
		map_to_send["main_scene_log"] <- main_scene_metrics(main_scene_stage);
'@
            $text = Replace-One $text $old $new
            $old = @'
			dykes_built_by_players[each_dyke.name] <-player_id;
'@
            $new = @'
			dykes_built_by_players[each_dyke.name] <-player_id;
			map study_event <- main_scene_metrics("barrier_built");
			study_event["player_id"] <- player_id;
			study_event["success"] <- true;
			study_event["barrier"] <- main_scene_barrier_record(each_dyke);
			study_event["request_start_gama"] <- [converted_start_point.x, converted_start_point.y, converted_start_point.z];
			study_event["request_end_gama"] <- [converted_end_point.x, converted_end_point.y, converted_end_point.z];
			do send_message players: unity_player as list mes: ["main_scene_log" :: study_event];
'@
            $text = Replace-One $text $old $new
            $old = @'
	action destroy_dyke_with_unity(string player_id, string id) {
'@
            $new = @'
	action destroy_dyke_with_unity(string player_id, string id) {
		dyke study_target <- dyke first_with (each.name = id);
		map study_barrier <- study_target = nil ? nil : main_scene_barrier_record(study_target);
'@
            $text = Replace-One $text $old $new
            $old = @'
			player_built_dykes[builder] >> id;
'@
            $new = @'
			player_built_dykes[builder] >> id;
			map study_event <- main_scene_metrics("barrier_destroyed");
			study_event["player_id"] <- player_id;
			study_event["success"] <- study_barrier != nil and possible_length > 0;
			study_event["barrier"] <- study_barrier;
			do send_message players: unity_player as list mes: ["main_scene_log" :: study_event];
'@
            $text = Replace-One $text $old $new
        }
        $prepared = Join-Path $out $name
        [IO.File]::WriteAllText($prepared, $text, $encoding)
        $manifest += [pscustomobject]@{
            destination = $path
            original_sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            prepared = $location.name + '/' + $name
            prepared_sha256 = (Get-FileHash -LiteralPath $prepared -Algorithm SHA256).Hash
        }
    }
}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'prepared-files.json'), (ConvertTo-Json -InputObject $manifest -Depth 4), $encoding)
