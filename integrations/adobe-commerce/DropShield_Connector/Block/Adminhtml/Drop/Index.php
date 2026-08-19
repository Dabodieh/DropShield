<?php

declare(strict_types=1);

namespace DropShield\Connector\Block\Adminhtml\Drop;

use DropShield\Connector\Model\ProtectedDropRepository;
use Magento\Backend\Block\Template;

class Index extends Template
{
    public function __construct(Template\Context $context, private readonly ProtectedDropRepository $repository, array $data = [])
    {
        parent::__construct($context, $data);
    }

    /** @return array<int, array{drop: \DropShield\Connector\Model\ProtectedDrop, count:int}> */
    public function getRows(): array
    {
        return array_map(fn ($drop): array => ['drop' => $drop, 'count' => count($this->repository->getProductIds($drop->entityId))], $this->repository->getAll());
    }
}
