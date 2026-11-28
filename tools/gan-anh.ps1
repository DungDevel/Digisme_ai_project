param(
  [string]$BaseUrl  = "http://localhost:5000",
  [string]$ImageDir = (Join-Path $PSScriptRoot "anh-goc"),
  [switch]$Force
)
Add-Type -AssemblyName System.Net.Http

function Norm($s) {
  $d = $s.Normalize([Text.NormalizationForm]::FormD)
  $d = [regex]::Replace($d, '\p{Mn}', '')
  $d = $d.Replace([string][char]0x0111, 'd').Replace([string][char]0x0110, 'D')
  ($d.ToLower() -replace '[^a-z0-9]', '')
}

$map = @{
  'banhkinhdosocola'='banhkinhdosocola.jpg'; 'banhquyoreo'='banhquyoreo.jpg'; 'banhtrangre300g'='banhtrangre.jpg'
  'baonilon40x50'='baonilon.jpg'; 'chainuoc2l'='chainuoc2l.png'; 'cocacola330ml'='cocacola.jpg'
  'dauanneptune1l'='dauan.jpg'; 'duongcat1kg'='duongcat.jpg'; 'kemdanhrangps'='kemdanhrang.jpg'
  'muoihat250g'='muoihat.jpg'; 'nuocmamnamngu500ml'='nuocmannamngu.jpg'; 'nuocmia450ml'='nuocmia.jpg'
  'nuocruachensunlight'='nuocruachen.jpg'; 'nuocsuoilavie500ml'='nuocsuoilavile.jpg'; 'pepsi330ml'='pepsi.jpg'
  'redbull250ml'='redbull.jpg'; 'socolanutella'='socolanutela.jpg'; 'suachuathtrue'='suachuathtruemilk.jpg'
  'suatuoivinamilk1l'='suatuoivinamil.jpg'; 'xaphonglifebuoy'='xaphonglifeboy.jpg'
}

# Dang nhap (Manager)
$user = Read-Host "Ten dang nhap Manager"
$sec  = Read-Host "Mat khau" -AsSecureString
$pw   = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec))
$login = Invoke-RestMethod -Uri "$BaseUrl/api/v1/auth/login" -Method Post -ContentType "application/json" -Body (@{ username=$user; password=$pw } | ConvertTo-Json)
$pw = $null
$token = $login.accessToken
$h = @{ Authorization = "Bearer $token" }

# Lay toan bo san pham
$all = @(); $page = 1; $tp = 1
do {
  $r = Invoke-RestMethod -Uri "$BaseUrl/api/v1/products?page=$page&pageSize=20" -Headers $h
  if ($r.items) { $all += $r.items; $tp = $r.totalPages } else { $all += $r; $tp = 1 }
  $page++
} while ($page -le $tp)
"Da tai " + $all.Count + " san pham"

function Upload-Image($id, $path) {
  $client = New-Object System.Net.Http.HttpClient
  $client.DefaultRequestHeaders.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", $token)
  $ct = switch ([IO.Path]::GetExtension($path).ToLower()) { '.png' {'image/png'} '.webp' {'image/webp'} '.gif' {'image/gif'} default {'image/jpeg'} }
  $fc = New-Object System.Net.Http.ByteArrayContent(,[IO.File]::ReadAllBytes($path))
  $fc.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse($ct)
  $form = New-Object System.Net.Http.MultipartFormDataContent
  $form.Add($fc, "imageFile", [IO.Path]::GetFileName($path))
  try {
    $resp = $client.PostAsync("$BaseUrl/api/v1/products/$id/image", $form).Result
    [pscustomobject]@{ id=$id; status=[int]$resp.StatusCode; body=$resp.Content.ReadAsStringAsync().Result }
  } catch { [pscustomobject]@{ id=$id; status=0; body=$_.Exception.Message } }
  finally { $client.Dispose() }
}

$ok = 0; $skip = 0; $fail = @()
foreach ($p in $all) {
  if ($p.barcode -notlike '000000*') { continue }
  $k = Norm ($p.name -replace ' - SP\d+$','')
  if (-not $map.ContainsKey($k)) { continue }
  if ($p.imageUrl -and -not $Force) { $skip++; continue }
  $file = Join-Path $ImageDir $map[$k]
  if (-not (Test-Path $file)) { $fail += [pscustomobject]@{ id=$p.id; status=0; body="Thieu file $file" }; continue }
  $res = Upload-Image $p.id $file
  if ($res.status -eq 200) { $ok++ } else { $fail += $res }
}
"Thanh cong: $ok | Bo qua (da co anh): $skip | Loi: " + $fail.Count
$fail | Format-Table id, status, body -AutoSize -Wrap