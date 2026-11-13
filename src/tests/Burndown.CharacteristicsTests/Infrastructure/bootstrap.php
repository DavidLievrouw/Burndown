<?php
// Bootstraps a throwaway Firefly III instance: first user, personal access client, group membership and a token.
// Output contains a line "TOKEN:<jwt>"; Firefly's own log lines may surround it.
require '/var/www/html/vendor/autoload.php';
$app = require '/var/www/html/bootstrap/app.php';
$kernel = $app->make(Illuminate\Contracts\Console\Kernel::class);
$kernel->bootstrap();

$repository = app(FireflyIII\Repositories\User\UserRepositoryInterface::class);
$user = $repository->store([
    'blocked' => false,
    'blocked_code' => null,
    'email' => 'tests@example.test',
    'role' => 'owner',
]);
$user->password = Illuminate\Support\Facades\Hash::make(bin2hex(random_bytes(16)));
$user->save();

$kernel->call('passport:client', [
    '--personal' => true,
    '--name' => 'burndown-tests',
    '--provider' => 'users',
    '--no-interaction' => true,
]);
$kernel->call('correction:create-group-memberships');

$user = FireflyIII\User::query()->first();
echo 'TOKEN:' . $user->createToken('burndown-tests')->accessToken . "\n";
